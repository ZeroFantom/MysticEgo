package ru.isdarkline.mysticego

import android.app.Activity
import android.app.ActivityManager
import android.content.Intent
import android.graphics.Color
import android.net.Uri
import android.os.BatteryManager
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.os.Process
import android.os.StatFs
import android.view.WindowInsets
import android.view.WindowManager
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import com.google.ai.edge.litertlm.Backend
import com.google.ai.edge.litertlm.Engine
import com.google.ai.edge.litertlm.EngineConfig
import com.google.ai.edge.litertlm.ConversationConfig
import java.io.File
import java.io.FileOutputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

/** Runs in a separate private app process to contain native-engine crashes and memory pressure.
 * This is process isolation, not a security sandbox or a different Android UID. */
class EmbeddedModelActivity : Activity() {
    private val worker = Executors.newSingleThreadExecutor()
    private val running = AtomicBoolean(false)
    private lateinit var root: LinearLayout
    private lateinit var status: TextView
    private lateinit var checksum: EditText
    private lateinit var prompt: EditText
    private lateinit var importButton: Button
    private lateinit var generateButton: Button
    private lateinit var removeButton: Button
    private lateinit var stopButton: Button
    private val directory get() = File(noBackupFilesDir, "local-model")
    private val model get() = File(directory, "selected.litertlm")
    private val partial get() = File(directory, "import.partial")
    private val cache get() = File(cacheDir, "local-model")
    private var expected = ""
    @Volatile private var visible = false

    override fun onCreate(state: Bundle?) {
        super.onCreate(state)
        window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        directory.mkdirs()
        partial.delete()
        val scroll = ScrollView(this).apply { isFillViewport = true; setBackgroundColor(Color.rgb(23,19,39)) }
        root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(20),dp(24),dp(20),dp(24)) }
        scroll.addView(root)
        setContentView(scroll)
        scroll.setOnApplyWindowInsetsListener { view, insets ->
            if (Build.VERSION.SDK_INT >= 30) {
                val safe = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
                view.setPadding(safe.left,safe.top,safe.right,safe.bottom)
            } else view.setPadding(insets.systemWindowInsetLeft,insets.systemWindowInsetTop,insets.systemWindowInsetRight,insets.systemWindowInsetBottom)
            insets
        }
        scroll.requestApplyInsets()
        label("MysticEgo · Mobile AI Runtime",24)
        label("Экспериментальный автономный движок LiteRT-LM. Импортируйте доверенную текстовую модель .litertlm до 1,5 ГиБ и её SHA-256 из доверенного источника. Модель хранится только на телефоне. Ответ генерируется на CPU, без сетевого запроса или автоматического перехода в облако.",15)
        checksum = field("SHA-256 модели (64 символа)")
        importButton = button("Выбрать файл модели") {
            val digest = checksum.text.toString().trim()
            if (!LocalInferencePolicy.validDigest(digest)) {status.text="Введите SHA-256 из доверенного источника.";return@button}
            expected = digest.lowercase(java.util.Locale.ROOT)
            val intent = Intent(Intent.ACTION_OPEN_DOCUMENT).apply { addCategory(Intent.CATEGORY_OPENABLE); type="application/octet-stream" }
            try { startActivityForResult(intent,100) } catch (_:Exception) { status.text="Системный выбор файла недоступен." }
        }
        prompt = field("Сообщение (до 800 символов)")
        prompt.minLines=3
        generateButton=button("Спросить встроенную модель"){generate()}
        removeButton=button("Удалить импортированную модель"){
            android.app.AlertDialog.Builder(this).setTitle("Удалить локальную копию модели?")
                .setMessage("Исходный выбранный файл не затрагивается.")
                .setPositiveButton("Удалить"){_,_-> if(!running.get()){model.delete();cache.deleteRecursively();refresh("Локальная копия удалена.")}}
                .setNegativeButton("Отмена",null).show()
        }
        stopButton=button("Остановить вычисление и закрыть"){terminate()}
        button("Вернуться"){finish()}
        status=label("",16).apply { setTextIsSelectable(true) }
        refresh()
    }
    private fun dp(n:Int)=(n*resources.displayMetrics.density).toInt()
    private fun label(value:String,size:Int)=TextView(this).apply {text=value;textSize=size.toFloat();setTextColor(Color.rgb(230,224,250));setPadding(0,dp(12),0,dp(8));root.addView(this)}
    private fun field(hintText:String)=EditText(this).apply {hint=hintText;setTextColor(Color.WHITE);setHintTextColor(Color.rgb(183,172,209));isSaveEnabled=false;root.addView(this)}
    private fun button(title:String,action:()->Unit)=Button(this).apply {text=title;setOnClickListener{action()};root.addView(this)}
    private fun warning(bytes:Long):String? {
        if(!Build.SUPPORTED_64_BIT_ABIS.contains("arm64-v8a"))return "В пилоте поддерживаются только ARM64-устройства."
        val memory=ActivityManager.MemoryInfo()
        (getSystemService(ACTIVITY_SERVICE) as ActivityManager).getMemoryInfo(memory)
        val battery=(getSystemService(BATTERY_SERVICE) as BatteryManager).getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY)
        val thermal=if(Build.VERSION.SDK_INT>=29)(getSystemService(POWER_SERVICE) as PowerManager).currentThermalStatus else -1
        return LocalInferencePolicy.block(bytes,memory.availMem,memory.lowMemory,battery,thermal)
    }
    private fun refresh(message:String=""){
        val busy=running.get()
        importButton.isEnabled=!busy
        checksum.isEnabled=!busy
        removeButton.isEnabled=!busy&&model.isFile
        generateButton.isEnabled=!busy&&model.isFile
        stopButton.isEnabled=busy
        status.text=if(message.isNotEmpty())message else if(model.isFile)"Модель импортирована: ${model.length()/LocalInferencePolicy.MIB} МиБ. ${warning(model.length())?:"Готово к пробному запуску."}" else "Модель не импортирована."
    }
    @Deprecated("Android document picker callback")
    override fun onActivityResult(requestCode:Int,resultCode:Int,data:Intent?){
        super.onActivityResult(requestCode,resultCode,data)
        if(requestCode!=100||resultCode!=RESULT_OK)return
        val uri=data?.data?:return
        if(uri.scheme!="content"||!LocalInferencePolicy.validDigest(expected)||!running.compareAndSet(false,true))return
        val digest=expected;expected=""
        refresh("Проверка и импорт модели…")
        worker.execute {
            var message="Импорт отклонён: файл повреждён, превышает лимит или не совпадает SHA-256."
            try {
                val hash=MessageDigest.getInstance("SHA-256")
                val space=StatFs(directory.absolutePath).availableBytes
                var count=0L
                contentResolver.openInputStream(uri)?.use { input ->
                    FileOutputStream(partial).use { out ->
                        val buffer=ByteArray(1024*1024)
                        while(true){
                            val n=input.read(buffer)
                            if(n<0)break
                            if(n==0)continue
                            count+=n
                            if(!LocalInferencePolicy.canImport(count,space))throw java.io.IOException("Size or storage limit")
                            hash.update(buffer,0,n);out.write(buffer,0,n)
                        }
                        out.fd.sync()
                    }
                }?:throw java.io.IOException("No input")
                val actual=hash.digest().joinToString(""){ "%02x".format(it.toInt() and 255) }
                if(count==0L||actual!=digest)throw java.io.IOException("Digest mismatch")
                Files.move(partial.toPath(),model.toPath(),StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING)
                cache.deleteRecursively()
                message="Модель импортирована и проверена. Теперь можно проверить её совместимость."
            }catch(_:Exception){}finally{partial.delete();running.set(false)}
            val result=message
            runOnUiThread{if(!isFinishing&&!isDestroyed)refresh(result)}
        }
    }
    private fun generate(){
        val text=prompt.text.toString().trim()
        if(text.isEmpty()||text.length>800||!model.isFile){refresh("Введите сообщение до 800 символов и импортируйте модель.");return}
        val block=warning(model.length())
        if(block!=null){refresh("$block. Выберите серверный режим вручную.");return}
        if(!visible||!running.compareAndSet(false,true))return
        refresh("Загрузка модели и генерация…")
        worker.execute {
            var result="Модель не поддерживается устройством или движком. Запрос в облако не отправлялся."
            try{
                cache.mkdirs()
                Engine(EngineConfig(modelPath=model.absolutePath,backend=Backend.CPU(),maxNumTokens=2048,cacheDir=cache.absolutePath)).use { engine ->
                    engine.initialize()
                    engine.createConversation(ConversationConfig(automaticToolCalling=false,maxOutputToken=512)).use { conversation ->
                        result=conversation.sendMessage(text).text.take(8000)
                    }
                }
            }catch(_:Exception){}catch(_:LinkageError){}finally{running.set(false)}
            val answer=result
            runOnUiThread{if(!isFinishing&&!isDestroyed)refresh(answer)}
        }
    }
    // A synchronous native inference cannot be promised to cancel instantly. Terminate only this dedicated process.
    private fun terminate(){finishAndRemoveTask();Process.killProcess(Process.myPid())}
    override fun onStart(){super.onStart();visible=true}
    override fun onStop(){visible=false;if(running.get())terminate();super.onStop()}
    override fun onDestroy(){worker.shutdownNow();super.onDestroy()}
}
