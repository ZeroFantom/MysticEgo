package ru.isdarkline.mysticego;

import android.app.Activity;
import android.os.Bundle;
import android.graphics.Color;
import android.text.InputType;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.view.WindowInsets;
import android.os.Build;
import org.json.JSONObject;
import org.json.JSONArray;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.Proxy;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Explicit local text-only requests to a user-started loopback model server. No WebView bridge. */
public final class LocalModelActivity extends Activity {
    private final ExecutorService executor=Executors.newSingleThreadExecutor();
    private volatile HttpURLConnection connection;
    private volatile int generation;
    private volatile boolean destroyed;
    private EditText port, model, key, prompt;
    private TextView answer;
    private Button send;

    @Override public void onCreate(Bundle state){
        super.onCreate(state);
        ScrollView scroll=new ScrollView(this);
        LinearLayout layout=new LinearLayout(this);layout.setOrientation(LinearLayout.VERTICAL);
        layout.setBackgroundColor(Color.rgb(23,19,39));layout.setPadding(dp(20),dp(24),dp(20),dp(24));
        scroll.setBackgroundColor(Color.rgb(23,19,39));scroll.setFillViewport(true);
        scroll.setOnApplyWindowInsetsListener((view,insets)->{
            if(Build.VERSION.SDK_INT>=30){
                android.graphics.Insets safe=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());
                view.setPadding(safe.left,safe.top,safe.right,safe.bottom);
            }else view.setPadding(insets.getSystemWindowInsetLeft(),insets.getSystemWindowInsetTop(),insets.getSystemWindowInsetRight(),insets.getSystemWindowInsetBottom());
            return insets;
        });
        label(layout,"MysticEgo · Локальный ИИ",24);
        label(layout,"Запустите PocketPal AI или llama.cpp с API на 127.0.0.1. Укажите порт, ID загруженной модели и ключ API. Запрос не отправляется на Orchestra или в облако. Используйте только доверенный локальный сервер.",15);
        port=field(layout,"Порт API",InputType.TYPE_CLASS_NUMBER);port.setText("8080");
        model=field(layout,"ID модели",InputType.TYPE_CLASS_TEXT);
        key=field(layout,"Ключ локального API",InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_PASSWORD);
        prompt=field(layout,"Сообщение",InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_FLAG_MULTI_LINE);prompt.setMinLines(3);
        send=new Button(this);send.setText("Спросить локальную модель");send.setOnClickListener(v->ask());layout.addView(send);
        Button cancel=new Button(this);cancel.setText("Отменить");cancel.setOnClickListener(v->cancel());layout.addView(cancel);
        answer=label(layout,"",16);answer.setTextIsSelectable(true);
        Button back=new Button(this);back.setText("Вернуться");back.setOnClickListener(v->finish());layout.addView(back);
        scroll.addView(layout);setContentView(scroll);scroll.requestApplyInsets();
    }
    private int dp(int n){return Math.round(n*getResources().getDisplayMetrics().density);}
    private TextView label(LinearLayout root,String value,int size){TextView v=new TextView(this);v.setText(value);v.setTextColor(Color.rgb(230,224,250));v.setTextSize(size);v.setPadding(0,dp(12),0,dp(8));root.addView(v);return v;}
    private EditText field(LinearLayout root,String hint,int type){
        EditText v=new EditText(this);v.setInputType(type);v.setHint(hint);v.setContentDescription(hint);v.setTextColor(Color.WHITE);v.setHintTextColor(Color.rgb(183,172,209));v.setSaveEnabled(false);root.addView(v);return v;
    }
    private void ask(){
        if(!send.isEnabled())return;
        final int p;try{p=Integer.parseInt(port.getText().toString());if(p<1024||p>65535)throw new NumberFormatException();}catch(NumberFormatException e){answer.setText("Введите порт 1024–65535.");return;}
        final String id=model.getText().toString().trim(),secret=key.getText().toString(),text=prompt.getText().toString().trim();
        if(id.isEmpty()||id.length()>200||secret.trim().isEmpty()||secret.length()>500||secret.indexOf('\r')>=0||secret.indexOf('\n')>=0||text.isEmpty()||text.length()>4000){answer.setText("Нужны ID модели, ключ и текст до 4000 символов.");return;}
        final int ticket=++generation;send.setEnabled(false);answer.setText("Локальная модель отвечает…");
        executor.execute(()->{
            HttpURLConnection http=null;String result;
            try{
                if(ticket!=generation||destroyed)return;
                http=(HttpURLConnection)new URL("http://127.0.0.1:"+p+"/v1/chat/completions").openConnection(Proxy.NO_PROXY);
                connection=http;if(ticket!=generation||destroyed)return;
                http.setInstanceFollowRedirects(false);http.setConnectTimeout(5000);http.setReadTimeout(90000);http.setRequestMethod("POST");http.setDoOutput(true);
                http.setRequestProperty("Authorization","Bearer "+secret);http.setRequestProperty("Content-Type","application/json");
                JSONObject body=new JSONObject().put("model",id).put("stream",false).put("max_tokens",512).put("messages",new JSONArray().put(new JSONObject().put("role","system").put("content","Ты локальный ассистент MysticEgo. Отвечай текстом. Не выполняй действия.")).put(new JSONObject().put("role","user").put("content",text)));
                byte[] payload=body.toString().getBytes(StandardCharsets.UTF_8);http.setFixedLengthStreamingMode(payload.length);
                try(java.io.OutputStream output=http.getOutputStream()){output.write(payload);}
                if(http.getResponseCode()!=200)throw new java.io.IOException("Local API error");
                try(InputStream input=http.getInputStream();ByteArrayOutputStream buffer=new ByteArrayOutputStream()){
                    byte[] block=new byte[8192];int n;
                    while((n=input.read(block))!=-1){if(buffer.size()+n>1024*1024||ticket!=generation)throw new java.io.IOException("Response limit");buffer.write(block,0,n);}
                    result=new JSONObject(new String(buffer.toByteArray(),StandardCharsets.UTF_8)).getJSONArray("choices").getJSONObject(0).getJSONObject("message").getString("content");
                    if(result.length()>8000)result=result.substring(0,8000);
                }
            }catch(Exception ex){result="Локальная модель недоступна. Проверьте сервер, модель, порт и ключ. Облачный запрос не выполнялся.";}
            finally{if(http!=null)http.disconnect();if(connection==http)connection=null;}
            final String response=result;runOnUiThread(()->{if(!destroyed&&generation==ticket){answer.setText(response);send.setEnabled(true);}});
        });
    }
    private void cancel(){generation++;HttpURLConnection active=connection;if(active!=null)active.disconnect();if(send!=null)send.setEnabled(true);if(answer!=null)answer.setText("Запрос отменён.");}
    @Override protected void onStop(){cancel();if(key!=null)key.setText("");super.onStop();}
    @Override protected void onDestroy(){destroyed=true;cancel();executor.shutdownNow();super.onDestroy();}
}
