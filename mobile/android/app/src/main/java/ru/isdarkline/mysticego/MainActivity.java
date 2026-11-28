package ru.isdarkline.mysticego;

import androidx.activity.ComponentActivity;
import androidx.activity.OnBackPressedCallback;
import android.graphics.Bitmap;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.net.http.SslError;
import android.os.Build;
import android.os.Bundle;
import android.view.Gravity;
import android.view.WindowInsets;
import android.webkit.CookieManager;
import android.webkit.GeolocationPermissions;
import android.webkit.PermissionRequest;
import android.webkit.SslErrorHandler;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.ScrollView;
import android.content.pm.PackageManager;
import android.Manifest;
import android.app.ActivityManager;
import android.os.StatFs;
import android.util.DisplayMetrics;
import java.util.Locale;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;
import com.google.zxing.integration.android.IntentIntegrator;
import com.google.zxing.integration.android.IntentResult;

/** Minimal Android client. Auth remains inside the trusted first-party web application. */
public final class MainActivity extends ComponentActivity {
    private WebView web;
    private static final String INIT_VERSION = "mobile-init-1";
    private static final int CAMERA_REQUEST = 310;
    private CheckBox initAcceptance;
    private Button initCamera;
    private boolean cameraReviewed;
    private PermissionRequest mediaRequest;
    private String mediaResource;
    private static final int MEDIA_REQUEST=311;
    private static final String RELEASES = "https://github.com/ZeroFantom/MysticEgo/releases";

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getOnBackPressedDispatcher().addCallback(this,new OnBackPressedCallback(true){
            @Override public void handleOnBackPressed(){
                if(web!=null&&web.canGoBack())web.goBack();
                else {setEnabled(false);try{getOnBackPressedDispatcher().onBackPressed();}finally{setEnabled(true);}}
            }
        });
        if (!initialized()) { showInitialization(); return; }
        showWorkspace();
    }

    private boolean initialized() {
        return INIT_VERSION.equals(getPreferences(MODE_PRIVATE).getString("initialization_version", ""));
    }

    private String deviceReport() {
        DisplayMetrics screen = getResources().getDisplayMetrics();
        ActivityManager.MemoryInfo memory = new ActivityManager.MemoryInfo();
        ((ActivityManager)getSystemService(ACTIVITY_SERVICE)).getMemoryInfo(memory);
        StatFs storage = new StatFs(getFilesDir().getAbsolutePath());
        return "Устройство: " + Build.MANUFACTURER + " " + Build.MODEL + "\nAndroid: " + Build.VERSION.RELEASE + " (API " + Build.VERSION.SDK_INT + ")\nПатч безопасности: " + Build.VERSION.SECURITY_PATCH + "\nCPU ABI: " + String.join(", ", Build.SUPPORTED_ABIS) + "\nЭкран: " + screen.widthPixels + " × " + screen.heightPixels + " px, " + screen.densityDpi + " dpi\nПамять: " + memory.totalMem / 1048576 + " МиБ\nДоступно для приложения: " + storage.getAvailableBytes() / 1048576 + " МиБ\nЯзык: " + Locale.getDefault().toLanguageTag() + "\nВерсия приложения: " + appVersion();
    }

    private String appVersion() {
        try { return getPackageManager().getPackageInfo(getPackageName(), 0).versionName; }
        catch (PackageManager.NameNotFoundException ex) { return "unknown"; }
    }

    private void showInitialization() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(Color.rgb(23, 19, 39));
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(dp(24), dp(48), dp(24), dp(36));
        TextView title = new TextView(this);title.setText("MysticEgo Mobile\nПервая инициализация");title.setTextSize(26);title.setTextColor(Color.WHITE);layout.addView(title);
        TextView description = new TextView(this);description.setText("Проверьте профиль устройства и условия. Техническая диагностика ниже остаётся на телефоне; серверу при входе передаются тип клиента, версия Android и название модели из User-Agent. Камера нужна только для QR; отказаться можно без потери обычного входа. Другие разрешения не запрашиваются, пока соответствующие функции не реализованы. Системные разрешения можно отозвать в настройках Android.\n\n" + deviceReport());description.setTextColor(Color.rgb(224,218,244));description.setTextSize(15);description.setPadding(0,dp(20),0,dp(20));layout.addView(description);
        initAcceptance = new CheckBox(this);initAcceptance.setText("Я ознакомился с назначением технических данных и разрешений приложения. При регистрации отдельно принимаются условия сервиса, политика обработки данных и лицензия.");initAcceptance.setTextColor(Color.WHITE);layout.addView(initAcceptance);
        cameraReviewed = checkSelfPermission(Manifest.permission.CAMERA)==PackageManager.PERMISSION_GRANTED;
        initCamera = button(cameraReviewed?"Камера: разрешена":"Разрешить камеру для QR", "Camera permission", () -> requestPermissions(new String[]{Manifest.permission.CAMERA}, CAMERA_REQUEST));layout.addView(initCamera);
        Button skip=button("Без камеры", "Skip optional camera", () -> {cameraReviewed=true;initCamera.setText("Камера: выбор сделан");});layout.addView(skip);
        Button proceed=button("Завершить инициализацию", "Complete initialization", () -> {
            if(!initAcceptance.isChecked()||!cameraReviewed){Toast.makeText(this,"Подтвердите ознакомление и выберите доступ к камере.",Toast.LENGTH_LONG).show();return;}
            getPreferences(MODE_PRIVATE).edit().putString("initialization_version",INIT_VERSION).putLong("initialization_accepted_at",System.currentTimeMillis()).apply();
            showWorkspace();
        });layout.addView(proceed);scroll.addView(layout);setContentView(scroll);
    }

    @Override public void onRequestPermissionsResult(int requestCode,String[] permissions,int[] results) {
        super.onRequestPermissionsResult(requestCode,permissions,results);
        if(requestCode==MEDIA_REQUEST){finishMediaPermission();return;}
        if(requestCode==CAMERA_REQUEST && initCamera!=null){cameraReviewed=true;initCamera.setText(checkSelfPermission(Manifest.permission.CAMERA)==PackageManager.PERMISSION_GRANTED?"Камера: разрешена":"Камера: не разрешена");}
    }

    private void showWorkspace() {
        initCamera=null;initAcceptance=null;
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.rgb(23, 19, 39));
        root.setOnApplyWindowInsetsListener((view, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets safe = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                view.setPadding(safe.left, safe.top, safe.right, safe.bottom);
            } else {
                view.setPadding(insets.getSystemWindowInsetLeft(), insets.getSystemWindowInsetTop(),
                    insets.getSystemWindowInsetRight(), insets.getSystemWindowInsetBottom());
            }
            return insets;
        });

        LinearLayout bar = new LinearLayout(this);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        TextView title = new TextView(this);
        title.setText("MysticEgo");
        title.setTextColor(Color.WHITE);
        title.setTextSize(18);
        title.setPadding(dp(12), 0, dp(4), 0);
        root.addView(title, new LinearLayout.LayoutParams(-1, dp(36)));
        title.setGravity(Gravity.CENTER_VERTICAL);
        bar.addView(button("←", "Back", () -> { if (web.canGoBack()) web.goBack(); else web.loadUrl(MobileRoutes.HOME); }));
        bar.addView(button("⌂", "Home", () -> web.loadUrl(MobileRoutes.HOME)));
        bar.addView(button("QR", "Scan sign-in or friend QR", this::scanQr));
        bar.addView(button("AI", "Локальная модель", () -> startActivity(new Intent(this,LocalModelActivity.class))));
        bar.addView(button("MCP", "Mobile Agent", () -> startActivity(new Intent(this,MobileAgentActivity.class))));
        bar.addView(button("↻", "Reload", () -> web.reload()));
        android.widget.HorizontalScrollView actionScroll=new android.widget.HorizontalScrollView(this);
        actionScroll.setHorizontalScrollBarEnabled(false);actionScroll.setFillViewport(false);
        actionScroll.addView(bar);root.addView(actionScroll,new LinearLayout.LayoutParams(-1,-2));

        web = new WebView(this);
        WebView.setWebContentsDebuggingEnabled(false);
        WebSettings settings = web.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        settings.setJavaScriptCanOpenWindowsAutomatically(false);
        settings.setSupportMultipleWindows(false);
        settings.setGeolocationEnabled(false);
        settings.setSafeBrowsingEnabled(true);
        settings.setUserAgentString(settings.getUserAgentString() + " MysticEgoAndroid/1 MysticModel/" + safeModel());
        CookieManager.getInstance().setAcceptCookie(true);
        CookieManager.getInstance().setAcceptThirdPartyCookies(web, false);
        web.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                if (!request.isForMainFrame()) return false;
                String url = request.getUrl().toString();
                if (request.hasGesture() && "mysticego://spatial".equals(url)) { openSpatialBrowser(); return true; }
                if (MobileRoutes.trustedPage(url)) return false;
                if (request.hasGesture() && RELEASES.equals(url)) openReleasePage();
                else Toast.makeText(MainActivity.this, "External navigation blocked", Toast.LENGTH_SHORT).show();
                return true;
            }
            @Override public void onPageStarted(WebView view, String url, Bitmap favicon) {
                // Defense in depth for navigation paths not seen by shouldOverrideUrlLoading.
                if (!MobileRoutes.trustedPage(url)) {
                    view.stopLoading();
                    view.loadUrl(MobileRoutes.HOME);
                    return;
                }
                super.onPageStarted(view, url, favicon);
            }
            @Override public void onReceivedSslError(WebView view, SslErrorHandler handler, SslError error) {
                handler.cancel();
                Toast.makeText(MainActivity.this, "Secure connection failed", Toast.LENGTH_LONG).show();
            }
        });
        web.setWebChromeClient(new WebChromeClient() {
            @Override public void onPermissionRequest(PermissionRequest request) {
                runOnUiThread(()->askMediaPermission(request));
            }
            @Override public void onPermissionRequestCanceled(PermissionRequest request) {
                if(mediaRequest==request){mediaRequest=null;mediaResource=null;}
            }
            @Override public void onGeolocationPermissionsShowPrompt(String origin, GeolocationPermissions.Callback callback) {
                callback.invoke(origin, false, false);
            }
        });
        // No JavaScript bridge, no file picker, no downloads or privileged device actions.
        web.setDownloadListener((url, agent, disposition, mime, length) ->
            Toast.makeText(this, "Downloads are not supported in this preview", Toast.LENGTH_SHORT).show());
        root.addView(web, new LinearLayout.LayoutParams(-1, 0, 1));
        setContentView(root);
        root.requestApplyInsets();
        openIntent(getIntent());
    }

    private void askMediaPermission(PermissionRequest request) {
        String[] resources=request.getResources();
        if(web==null||!hasWindowFocus()||!MobileRoutes.trustedPage(web.getUrl())||
            !MobileRoutes.trustedPage(request.getOrigin().toString())||resources.length!=1||mediaRequest!=null){
            request.deny();return;
        }
        String resource=resources[0];
        String permission=PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(resource)?Manifest.permission.CAMERA:
            PermissionRequest.RESOURCE_AUDIO_CAPTURE.equals(resource)?Manifest.permission.RECORD_AUDIO:null;
        if(permission==null){request.deny();return;}
        mediaRequest=request;mediaResource=resource;
        new android.app.AlertDialog.Builder(this).setTitle("MysticEgo · Разрешение")
            .setMessage(permission.equals(Manifest.permission.CAMERA)?"Разрешить камеру для текущей AR-сцены?":"Разрешить запись голоса для текущего запроса?")
            .setPositiveButton("Разрешить",(d,w)->{if(mediaRequest!=request)return;if(checkSelfPermission(permission)==PackageManager.PERMISSION_GRANTED)finishMediaPermission();else requestPermissions(new String[]{permission},MEDIA_REQUEST);})
            .setNegativeButton("Отмена",(d,w)->denyMediaPermission()).setOnCancelListener(d->denyMediaPermission()).show();
    }
    private void finishMediaPermission(){
        PermissionRequest request=mediaRequest;String resource=mediaResource;
        mediaRequest=null;mediaResource=null;if(request==null||resource==null)return;
        String permission=PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(resource)?Manifest.permission.CAMERA:Manifest.permission.RECORD_AUDIO;
        if(web!=null&&MobileRoutes.trustedPage(web.getUrl())&&MobileRoutes.trustedPage(request.getOrigin().toString())&&checkSelfPermission(permission)==PackageManager.PERMISSION_GRANTED)request.grant(new String[]{resource});else request.deny();
    }
    private void denyMediaPermission(){if(mediaRequest!=null){mediaRequest.deny();mediaRequest=null;mediaResource=null;}}

    private String safeModel() {
        String model=(Build.MANUFACTURER+" "+Build.MODEL).replaceAll("[^A-Za-z0-9._ -]", "_").trim().replace(' ', '_');
        return model.substring(0,Math.min(80,model.length()));
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private Button button(String text, String description, Runnable action) {
        Button result = new Button(this);
        result.setText(text); result.setContentDescription(description);
        result.setMinWidth(dp(48)); result.setMinimumWidth(dp(48));
        result.setOnClickListener(view -> action.run());
        return result;
    }

    private void scanQr() {
        new IntentIntegrator(this).setDesiredBarcodeFormats(IntentIntegrator.QR_CODE)
            .setPrompt("Отсканируйте QR входа или друга MysticEgo").setBeepEnabled(false)
            .setBarcodeImageEnabled(false).setOrientationLocked(false).initiateScan();
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        IntentResult result = IntentIntegrator.parseActivityResult(requestCode, resultCode, data);
        if (result == null) { super.onActivityResult(requestCode, resultCode, data); return; }
        String value = result.getContents();
        if (value == null) return;
        String target=MobileQr.resolve(value);
        if (target != null && web != null) web.loadUrl(target);
        else Toast.makeText(this, "Некорректный QR MysticEgo", Toast.LENGTH_SHORT).show();
    }

    private void openSpatialBrowser() {
        new android.app.AlertDialog.Builder(this).setTitle("MysticEgo · Пространственный AR")
            .setMessage("Открыть защищённую страницу в Chrome? Вход в браузере отдельный; токены приложения не передаются.")
            .setPositiveButton("Открыть",(dialog,which)->{
                Intent external=new Intent(Intent.ACTION_VIEW,Uri.parse(MobileRoutes.ORIGIN+"/audit-portal/assistant?tab=spatial"));
                external.setPackage("com.android.chrome");
                try{startActivity(external);}
                catch(android.content.ActivityNotFoundException ex){Toast.makeText(this,"Для пространственного AR установите Google Chrome.",Toast.LENGTH_LONG).show();}
            }).setNegativeButton("Отмена",null).show();
    }

    private void openReleasePage() {
        try { startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(RELEASES))); }
        catch (android.content.ActivityNotFoundException ex) {
            Toast.makeText(this, "No browser installed", Toast.LENGTH_SHORT).show();
        }
    }

    private void openIntent(Intent intent) {
        String data = intent != null && Intent.ACTION_VIEW.equals(intent.getAction()) ? intent.getDataString() : null;
        web.loadUrl(MobileRoutes.resolve(data));
    }

    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        if(initialized()&&web!=null)openIntent(intent);
    }


    @Override protected void onStop() {
        denyMediaPermission();
        if(web!=null)CookieManager.getInstance().flush();
        super.onStop();
    }

    @Override protected void onDestroy() {
        denyMediaPermission();
        if (web != null) {
            web.stopLoading();
            if (web.getParent() instanceof android.view.ViewGroup) ((android.view.ViewGroup) web.getParent()).removeView(web);
            web.destroy();
            web = null;
        }
        super.onDestroy();
    }
}
