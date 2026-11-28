package ru.isdarkline.mysticego;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** The scoped native credential never enters the WebView. No backup or export. */
final class AgentCredential {
    private static final String ALIAS="mysticego.mobile.agent.v1";
    final String id,secret;
    AgentCredential(String id,String secret){this.id=id;this.secret=secret;}
    private static SharedPreferences prefs(Context c){return c.getSharedPreferences("mobile-agent-v1",Context.MODE_PRIVATE);}
    private static SecretKey key(boolean create)throws Exception{
        KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
        if(store.containsAlias(ALIAS))return (SecretKey)store.getKey(ALIAS,null);
        if(!create)throw new IllegalStateException("Missing key");
        KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setKeySize(256).build());
        return generator.generateKey();
    }
    static AgentCredential load(Context c)throws Exception{
        SharedPreferences p=prefs(c);String id=p.getString("id",null);
        if(id==null)return null;
        if(!validId(id))throw new IllegalStateException("Invalid device ID");
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key(false),new GCMParameterSpec(128,Base64.decode(p.getString("iv",""),Base64.NO_WRAP)));
        cipher.updateAAD(id.getBytes(StandardCharsets.UTF_8));
        String secret=new String(cipher.doFinal(Base64.decode(p.getString("secret",""),Base64.NO_WRAP)),StandardCharsets.UTF_8);
        if(!validSecret(secret))throw new IllegalStateException("Invalid key");
        return new AgentCredential(id,secret);
    }
    static boolean validId(String id){return id!=null&&id.matches("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");}
    static boolean validSecret(String secret){return secret!=null&&secret.matches("[0-9a-f]{64}");}
    static void save(Context c,AgentCredential value)throws Exception{
        if(!validId(value.id)||!validSecret(value.secret))throw new IllegalArgumentException();
        Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key(true));
        cipher.updateAAD(value.id.getBytes(StandardCharsets.UTF_8));
        String ciphertext=Base64.encodeToString(cipher.doFinal(value.secret.getBytes(StandardCharsets.UTF_8)),Base64.NO_WRAP);
        if(!prefs(c).edit().putString("id",value.id).putString("iv",Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP)).putString("secret",ciphertext).commit())throw new IllegalStateException("Save failed");
    }
    static void forget(Context c)throws Exception{
        if(!prefs(c).edit().clear().commit())throw new IllegalStateException("Clear failed");
        KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);if(store.containsAlias(ALIAS))store.deleteEntry(ALIAS);
    }
}
