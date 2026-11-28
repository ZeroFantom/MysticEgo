package ru.isdarkline.mysticego;

import android.app.Activity;
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
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;
import com.google.zxing.integration.android.IntentIntegrator;
import com.google.zxing.integration.android.IntentResult;

/** Minimal Android client. Auth remains inside the trusted first-party web application. */
public final class MainActivity extends Activity {
    private WebView web;
    private static final String RELEASES = "https://github.com/ZeroFantom/MysticEgo/releases";

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
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
        bar.addView(title, new LinearLayout.LayoutParams(0, dp(52), 1));
        bar.addView(button("←", "Back", () -> { if (web.canGoBack()) web.goBack(); else web.loadUrl(MobileRoutes.HOME); }));
        bar.addView(button("⌂", "Home", () -> web.loadUrl(MobileRoutes.HOME)));
        bar.addView(button("QR", "Scan sign-in QR", this::scanQr));
        bar.addView(button("↻", "Reload", () -> web.reload()));
        root.addView(bar);

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
        settings.setUserAgentString(settings.getUserAgentString() + " MysticEgoAndroid/1");
        CookieManager.getInstance().setAcceptCookie(true);
        CookieManager.getInstance().setAcceptThirdPartyCookies(web, false);
        web.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                if (!request.isForMainFrame()) return false;
                String url = request.getUrl().toString();
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
            @Override public void onPermissionRequest(PermissionRequest request) { request.deny(); }
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
            .setPrompt("Scan a MysticEgo sign-in QR code").setBeepEnabled(false)
            .setBarcodeImageEnabled(false).setOrientationLocked(false).initiateScan();
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        IntentResult result = IntentIntegrator.parseActivityResult(requestCode, resultCode, data);
        if (result == null) { super.onActivityResult(requestCode, resultCode, data); return; }
        String value = result.getContents();
        if (value == null) return;
        if (value.matches("https://orchestra\\.isdarkline\\.ru/audit-portal/account-security#qr=[a-f0-9]{64}")) {
            web.loadUrl(value);
        } else Toast.makeText(this, "Invalid MysticEgo sign-in QR", Toast.LENGTH_SHORT).show();
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
        openIntent(intent);
    }

    @Override public void onBackPressed() {
        if (web != null && web.canGoBack()) web.goBack(); else super.onBackPressed();
    }

    @Override protected void onStop() {
        CookieManager.getInstance().flush();
        super.onStop();
    }

    @Override protected void onDestroy() {
        if (web != null) {
            web.stopLoading();
            if (web.getParent() instanceof android.view.ViewGroup) ((android.view.ViewGroup) web.getParent()).removeView(web);
            web.destroy();
            web = null;
        }
        super.onDestroy();
    }
}
