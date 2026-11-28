package ru.isdarkline.mysticego;
import org.junit.Test;
import static org.junit.Assert.*;
public class MobileLocalActionsTest {
 @Test public void exactActionOnly(){
  for(String action:new String[]{"signal-device","open-app-settings","show-device-info"})assertEquals(action,MobileLocalActions.resolve("mysticego://local-action/"+action));
  for(String invalid:new String[]{null,"mysticego://local-action/shell","mysticego://local-action/open-app-settings?package=other","mysticego://local-action/show-device-info#anything","mysticego://local-action/%73ignal-device","mysticego://open/signal-device","https://orchestra.isdarkline.ru/signal-device"})assertNull(invalid,MobileLocalActions.resolve(invalid));
 }
}
