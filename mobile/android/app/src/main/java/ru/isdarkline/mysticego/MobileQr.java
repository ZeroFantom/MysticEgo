package ru.isdarkline.mysticego;
/** Fixed first-party QR routes. Scans never authenticate or send friend requests directly. */
final class MobileQr {
    private MobileQr() {}
    static String resolve(String value) {
        if(value==null || value.length()>240)return null;
        if(value.matches("https://orchestra\\.isdarkline\\.ru/audit-portal/account-security#qr=[a-f0-9]{64}"))return value;
        if(value.matches("https://orchestra\\.isdarkline\\.ru/audit-portal/collective\\?add=[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"))return value;
        return null;
    }
}
