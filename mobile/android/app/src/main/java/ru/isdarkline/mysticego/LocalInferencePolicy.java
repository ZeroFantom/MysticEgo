package ru.isdarkline.mysticego;

/** Conservative pilot limits, not a device compatibility guarantee. No network routing side effects. */
final class LocalInferencePolicy {
    static final long MIB=1024L*1024L;
    static final long MAX_MODEL=1536*MIB;
    static final long DISK_RESERVE=512*MIB;
    private LocalInferencePolicy(){}
    static boolean validDigest(String value){return value!=null&&value.matches("[0-9a-fA-F]{64}");}
    static boolean canImport(long bytes,long availableDisk){return bytes>0&&bytes<=MAX_MODEL&&availableDisk>=bytes+DISK_RESERVE;}
    static String block(long modelBytes,long availableRam,boolean lowMemory,int battery,int thermal){
        if(modelBytes<=0||modelBytes>MAX_MODEL)return "Неподдерживаемый размер модели";
        if(lowMemory||availableRam<modelBytes+1536*MIB)return "Недостаточно свободной оперативной памяти";
        if(battery<20||battery>100)return "Заряд ниже 20% или неизвестен";
        if(thermal>=3)return "Устройство перегрето";
        return null;
    }
}
