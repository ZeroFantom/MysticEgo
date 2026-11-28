package ru.isdarkline.mysticego;
import org.junit.Test;
import static org.junit.Assert.*;
public class LocalInferencePolicyTest {
    @Test public void resourceLimits(){
        long m=LocalInferencePolicy.MIB;
        assertTrue(LocalInferencePolicy.validDigest("a".repeat(64)));
        assertFalse(LocalInferencePolicy.validDigest("not-a-digest"));
        assertTrue(LocalInferencePolicy.canImport(700*m,1500*m));
        assertFalse(LocalInferencePolicy.canImport(700*m,1000*m));
        assertFalse(LocalInferencePolicy.canImport(2000*m,4000*m));
        assertNull(LocalInferencePolicy.block(700*m,2500*m,false,80,0));
        assertNotNull(LocalInferencePolicy.block(700*m,1500*m,false,80,0));
        assertNotNull(LocalInferencePolicy.block(700*m,2500*m,true,80,0));
        assertNotNull(LocalInferencePolicy.block(700*m,2500*m,false,19,0));
        assertNotNull(LocalInferencePolicy.block(700*m,2500*m,false,80,3));
    }
}
