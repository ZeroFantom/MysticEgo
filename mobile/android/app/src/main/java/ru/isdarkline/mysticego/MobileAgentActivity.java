package ru.isdarkline.mysticego;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.os.BatteryManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.text.InputType;
import android.view.WindowManager;
import android.view.WindowInsets;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.Executors;
import java.util.concurrent.ExecutorService;

public final class MobileAgentActivity extends Activity {
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final ExecutorService executor=Executors.newSingleThreadExecutor();
    private AgentCredential credential;
    private boolean resumed,receiving,busy,destroyed;
    private long epoch;
    private TextView status;
    private EditText code;
    private LinearLayout requests,layout;
    private Button toggle,connect,forget;
    private android.widget.CheckBox enrollmentConsent;
    private AlertDialog confirmation;
    private final Runnable pollTask=this::poll;

    @Override public void onCreate(Bundle state){
        super.onCreate(state);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);
        scroll.setBackgroundColor(Color.rgb(23,19,39));layout=new LinearLayout(this);layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(dp(20),dp(24),dp(20),dp(24));scroll.addView(layout);setContentView(scroll);
        scroll.setOnApplyWindowInsetsListener((view,insets)->{
            if(Build.VERSION.SDK_INT>=30){
                android.graphics.Insets safe=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());
                view.setPadding(safe.left,safe.top,safe.right,safe.bottom);
            }else view.setPadding(insets.getSystemWindowInsetLeft(),insets.getSystemWindowInsetTop(),insets.getSystemWindowInsetRight(),insets.getSystemWindowInsetBottom());
            return insets;
        });scroll.requestApplyInsets();
        label("MysticEgo · Mobile Agent",24);
        label("В безопасности аккаунта создайте одноразовый код регистрации, вставьте его здесь и свяжите телефон со своим аккаунтом. Ключ хранится отдельно от веб-сессии. Приём запросов работает только пока этот экран открыт; каждое действие подтверждается на телефоне.",15);
        code=new EditText(this);code.setHint("Код регистрации (64 символа)");code.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_PASSWORD);code.setSaveEnabled(false);layout.addView(code);
        enrollmentConsent=new android.widget.CheckBox(this);enrollmentConsent.setText("Я разрешаю отдельную регистрацию телефона, передачу модели и версии ОС и приём только подтверждённых мной действий.");enrollmentConsent.setTextColor(Color.WHITE);layout.addView(enrollmentConsent);
        enrollmentConsent.setOnCheckedChangeListener((v,checked)->render());
        connect=button("Зарегистрировать устройство",this::enroll);
        toggle=button("Включить приём",()->{if(receiving)stopReceive();else if(credential!=null){receiving=true;epoch++;render();poll();}});
        forget=button("Удалить локальный ключ",()->new AlertDialog.Builder(this).setTitle("Удалить ключ?")
            .setMessage("Сначала отзовите регистрацию в безопасности аккаунта. Удаление локального ключа само по себе не отзывает его на сервере.")
            .setPositiveButton("Удалить",(d,w)->{stopReceive();try{AgentCredential.forget(this);credential=null;status.setText("Локальный ключ удалён.");render();}catch(Exception e){status.setText("Не удалось удалить ключ.");}})
            .setNegativeButton("Отмена",null).show());
        status=label("",15);requests=new LinearLayout(this);requests.setOrientation(LinearLayout.VERTICAL);layout.addView(requests);
        try{credential=AgentCredential.load(this);}catch(Exception e){status.setText("Локальный ключ недоступен. Отзовите старое устройство в аккаунте и удалите ключ.");}
        render();
    }
    private int dp(int n){return Math.round(n*getResources().getDisplayMetrics().density);}
    private TextView label(String value,int size){TextView v=new TextView(this);v.setText(value);v.setTextColor(Color.rgb(230,224,250));v.setTextSize(size);v.setPadding(0,dp(12),0,dp(8));layout.addView(v);return v;}
    private Button button(String title,Runnable action){Button b=new Button(this);b.setText(title);b.setOnClickListener(v->action.run());layout.addView(b);return b;}
    private void render(){if(destroyed)return;boolean paired=credential!=null;code.setEnabled(!paired&&!busy);connect.setEnabled(!paired&&!busy&&enrollmentConsent.isChecked());enrollmentConsent.setEnabled(!paired&&!busy);toggle.setEnabled(paired&&resumed&&!busy);toggle.setText(receiving?"Остановить приём":"Включить приём");forget.setEnabled(!busy);if(paired&&!receiving&&!busy&&status.getText().length()==0)status.setText("Устройство зарегистрировано: "+credential.id);}
    private static Object call(String route,JSONObject body,AgentCredential agent)throws Exception{
        HttpURLConnection http=(HttpURLConnection)new URL(MobileRoutes.ORIGIN+"/api/auth/mobile-agent/"+route).openConnection();
        try{
            http.setInstanceFollowRedirects(false);http.setRequestMethod("POST");http.setConnectTimeout(8000);http.setReadTimeout(12000);http.setDoOutput(true);
            http.setRequestProperty("Content-Type","application/json");
            if(agent!=null)http.setRequestProperty("X-Mystic-Agent-Key",agent.secret);
            byte[] payload=body.toString().getBytes(StandardCharsets.UTF_8);http.setFixedLengthStreamingMode(payload.length);
            try(java.io.OutputStream out=http.getOutputStream()){out.write(payload);}
            int response=http.getResponseCode();if(response==401)throw new SecurityException("Agent unauthorized");if(response!=200)throw new java.io.IOException("Request failed");
            try(InputStream in=http.getInputStream();ByteArrayOutputStream data=new ByteArrayOutputStream()){
                byte[] buffer=new byte[4096];int n;while((n=in.read(buffer))!=-1){if(data.size()+n>65536)throw new java.io.IOException("Response too large");data.write(buffer,0,n);}
                return new org.json.JSONTokener(new String(data.toByteArray(),StandardCharsets.UTF_8)).nextValue();
            }
        }finally{http.disconnect();}
    }
    private static String limit(String value,int max){value=value.replaceAll("[\\p{Cntrl}]"," ").trim();return value.substring(0,Math.min(max,value.length()));}
    private void enroll(){
        if(busy||credential!=null||!resumed||!enrollmentConsent.isChecked())return;
        String ticket=code.getText().toString().trim();if(!AgentCredential.validSecret(ticket)){status.setText("Введите действующий код из безопасности аккаунта.");return;}
        code.setText("");busy=true;render();status.setText("Регистрация…");
        executor.execute(()->{
            AgentCredential stored=null;String error=null;
            try{
                JSONObject body=new JSONObject().put("consent",true).put("code",ticket).put("name",limit(Build.MANUFACTURER+" "+Build.MODEL,100)).put("os",limit("Android "+Build.VERSION.RELEASE,40));
                JSONObject result=(JSONObject)call("enroll",body,null);
                AgentCredential candidate=new AgentCredential(result.getString("id"),result.getString("secret"));
                AgentCredential.save(getApplicationContext(),candidate);stored=candidate;
            }catch(Exception e){error="Не удалось зарегистрировать устройство. Создайте новый код; проверьте список устройств в аккаунте.";}
            final AgentCredential value=stored;final String message=error;
            runOnUiThread(()->{if(destroyed)return;busy=false;credential=value;enrollmentConsent.setChecked(false);status.setText(value==null?message:"Устройство зарегистрировано. Включите приём вручную.");render();});
        });
    }
    private void poll(){
        if(!resumed||!receiving||busy||credential==null||destroyed)return;
        final long lease=epoch;final AgentCredential agent=credential;busy=true;render();
        executor.execute(()->{
            JSONArray result=null;String error=null;
            try{result=(JSONArray)call("poll",new JSONObject().put("id",agent.id),agent);}catch(SecurityException e){error="Ключ отозван или недействителен. Проверьте безопасность аккаунта.";}catch(Exception e){error="Сервер недоступен.";}
            final JSONArray rows=result;final String message=error;
            runOnUiThread(()->{if(destroyed)return;busy=false;if(lease!=epoch||!resumed||!receiving){render();return;}
                requests.removeAllViews();if(rows==null){status.setText(message);stopReceive();return;}
                status.setText("Приём включён · ожидающих запросов: "+rows.length());
                for(int i=0;i<rows.length();i++){JSONObject row=rows.optJSONObject(i);if(row==null)continue;String id=row.optString("id"),action=row.optString("action");if(!AgentCredential.validId(id)||title(action)==null)continue;
                    Button approve=new Button(this);approve.setText(title(action));approve.setOnClickListener(v->confirm(agent,id,action,lease));requests.addView(approve);}
                render();handler.postDelayed(pollTask,12000);
            });
        });
    }
    private static String title(String action){
        switch(action){case "battery-status":return "Показать уровень батареи";case "vibrate":return "Короткая вибрация";case "open-assistant":return "Открыть ассистента";default:return null;}
    }
    private void confirm(AgentCredential agent,String id,String action,long lease){
        if(!resumed||!receiving||lease!=epoch||busy||confirmation!=null)return;
        handler.removeCallbacks(pollTask);
        confirmation=new AlertDialog.Builder(this).setTitle("Запрос от вашего аккаунта").setMessage(title(action)+"? Действие произойдёт только после подтверждения.")
            .setPositiveButton("Разрешить",(d,w)->decide(agent,id,action,lease,true)).setNegativeButton("Отклонить",(d,w)->decide(agent,id,action,lease,false)).create();
        confirmation.setOnDismissListener(d->{confirmation=null;if(receiving&&resumed&&!busy)handler.postDelayed(pollTask,1000);});confirmation.show();
    }
    private void decide(AgentCredential agent,String id,String action,long lease,boolean approve){
        if(!resumed||!receiving||lease!=epoch||busy)return;
        busy=true;render();requests.removeAllViews();
        executor.execute(()->{
            boolean accepted=false;
            try{JSONObject r=(JSONObject)call("decide",new JSONObject().put("id",agent.id).put("requestId",id).put("approve",approve),agent);accepted=id.equals(r.optString("id"))&&action.equals(r.optString("action"))&&(approve?"accepted":"declined").equals(r.optString("state"));}catch(Exception ignored){}
            final boolean ok=accepted;
            runOnUiThread(()->{
                if(destroyed)return;
                if(!ok||!approve){busy=false;status.setText(ok?"Запрос отклонён.":"Запрос истёк или недоступен.");render();if(receiving&&resumed)handler.postDelayed(pollTask,1000);return;}
                String value=null;boolean success=false;
                if(resumed&&receiving&&lease==epoch&&hasWindowFocus()){
                    try{
                        if("battery-status".equals(action)){
                            BatteryManager battery=(BatteryManager)getSystemService(BATTERY_SERVICE);int level=battery.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY);
                            if(level>=0&&level<=100){value=Integer.toString(level);success=true;}
                        }else if("vibrate".equals(action)){
                            Vibrator vibrator=(Vibrator)getSystemService(VIBRATOR_SERVICE);
                            if(vibrator!=null&&vibrator.hasVibrator()){vibrator.vibrate(VibrationEffect.createOneShot(350,VibrationEffect.DEFAULT_AMPLITUDE));value="ok";success=true;}
                        }else if("open-assistant".equals(action)){
                            Intent launch=new Intent(this,MainActivity.class).setAction(Intent.ACTION_VIEW).setData(Uri.parse("mysticego://open/audit-assistant")).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP|Intent.FLAG_ACTIVITY_SINGLE_TOP);
                            startActivity(launch);value="ok";success=true;
                        }
                    }catch(RuntimeException ignored){}
                }
                final String result=value;final boolean completed=success;
                executor.execute(()->{
                    boolean reported=false;try{call("complete",new JSONObject().put("id",agent.id).put("requestId",id).put("success",completed).put("result",result==null?JSONObject.NULL:result),agent);reported=true;}catch(Exception ignored){}
                    final boolean receipt=reported;runOnUiThread(()->{if(destroyed)return;busy=false;status.setText(receipt?(completed?"Действие выполнено; результат отправлен.":"Действие не выполнено."):"Статус не доставлен; действие не повторяется.");render();if(receiving&&resumed)handler.postDelayed(pollTask,1000);});
                });
            });
        });
    }
    private void stopReceive(){receiving=false;epoch++;handler.removeCallbacks(pollTask);if(confirmation!=null)confirmation.dismiss();if(requests!=null)requests.removeAllViews();render();}
    @Override protected void onResume(){super.onResume();resumed=true;render();}
    @Override protected void onPause(){resumed=false;stopReceive();super.onPause();}
    @Override protected void onDestroy(){destroyed=true;stopReceive();executor.shutdown();super.onDestroy();}
}
