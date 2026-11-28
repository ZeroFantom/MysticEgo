package ru.isdarkline.mysticego;
import org.junit.Test;
import static org.junit.Assert.*;
public class MobileQrTest {
 @Test public void allowedRoutesOnly(){
  String friend=MobileRoutes.ORIGIN+"/audit-portal/collective?add=11111111-2222-3333-4444-555555555555";
  assertEquals(friend,MobileQr.resolve(friend));
  String login=MobileRoutes.ORIGIN+"/audit-portal/account-security#qr="+"a".repeat(64);
  assertEquals(login,MobileQr.resolve(login));
  for(String value:new String[]{null,friend+"&token=secret",friend.replace("isdarkline.ru","isdarkline.ru.evil"),friend.replace("https:","http:"),friend+"#bad",MobileRoutes.ORIGIN+"/audit-portal/commands"})assertNull(MobileQr.resolve(value));
 }
}
