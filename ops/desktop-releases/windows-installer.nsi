Unicode true
!include "MUI2.nsh"
Name "MysticEgo @PRODUCT@ @VERSION@"
OutFile "@OUTPUT@"
InstallDir "$LOCALAPPDATA\Programs\MysticEgo\@PRODUCT@\@VERSION@"
RequestExecutionLevel user
SetCompressor zlib
ShowInstDetails show
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "@SOURCE@/LICENSE"
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\launch.cmd"
!define MUI_FINISHPAGE_RUN_TEXT "Launch @PRODUCT@"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
Function .onInit
  StrCpy $INSTDIR "$LOCALAPPDATA\Programs\MysticEgo\@PRODUCT@\@VERSION@"
FunctionEnd
Function un.onInit
  StrCmp $INSTDIR "$LOCALAPPDATA\Programs\MysticEgo\@PRODUCT@\@VERSION@" safe
  Abort
  safe:
FunctionEnd
Section "Application"
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File /r "@SOURCE@/*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\MysticEgo"
  CreateShortCut "$SMPROGRAMS\MysticEgo\@PRODUCT@ @VERSION@.lnk" "$INSTDIR\launch.cmd"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MysticEgo-@PRODUCT@-@VERSION@" "DisplayName" "MysticEgo @PRODUCT@ @VERSION@"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MysticEgo-@PRODUCT@-@VERSION@" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MysticEgo-@PRODUCT@-@VERSION@" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MysticEgo-@PRODUCT@-@VERSION@" "NoRepair" 1
SectionEnd
Section "Uninstall"
  SetShellVarContext current
  Delete "$SMPROGRAMS\MysticEgo\@PRODUCT@ @VERSION@.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\MysticEgo-@PRODUCT@-@VERSION@"
  RMDir /r "$INSTDIR"
SectionEnd
