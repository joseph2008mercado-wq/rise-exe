Unicode true
RequestExecutionLevel user
ManifestDPIAware true
!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

Name "RISE.EXE"
OutFile "${SETUP_OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\RISE"
InstallDirRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\RISE" "InstallLocation"
Icon "..\src\Rise.App\Assets\rise.ico"
UninstallIcon "..\src\Rise.App\Assets\rise.ico"
SetCompressor zlib
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "RISE.EXE Setup"
VIAddVersionKey "FileDescription" "Install RISE.EXE for the current Windows user"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "RISE original code: Unlicense; NSIS: zlib license"
Var TestMode
Var RegistryKey
Var DesktopLink
Var StartLink

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\RISE.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch RISE.EXE"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  SetShellVarContext current
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "RISE.EXE requires 64-bit Windows."
    Abort
  ${EndIf}
  StrCpy $TestMode "0"
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/TESTMODE" $1
  ${IfNot} ${Errors}
    StrCpy $TestMode "1"
  ${EndIf}
FunctionEnd

Section "RISE.EXE"
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File "${PUBLISH_DIR}\RISE.exe"
  File "${PUBLISH_DIR}\LICENSE"
  File "${PUBLISH_DIR}\THIRD_PARTY_NOTICES.md"
  File "${PUBLISH_DIR}\SHA256SUMS.txt"
  SetOutPath "$INSTDIR\licenses"
  File "${PUBLISH_DIR}\licenses\*.txt"
  SetOutPath "$INSTDIR"
  ${If} $TestMode == "1"
    StrCpy $RegistryKey "Software\RISE\InstallerTest"
    CreateDirectory "$INSTDIR\TestShortcuts"
    StrCpy $DesktopLink "$INSTDIR\TestShortcuts\RISE.EXE.lnk"
    StrCpy $StartLink "$INSTDIR\TestShortcuts\Start-RISE.EXE.lnk"
    WriteINIStr "$INSTDIR\installer-test.ini" "Test" "Enabled" "1"
  ${Else}
    StrCpy $RegistryKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\RISE"
    CreateDirectory "$SMPROGRAMS\RISE.EXE"
    StrCpy $DesktopLink "$DESKTOP\RISE.EXE.lnk"
    StrCpy $StartLink "$SMPROGRAMS\RISE.EXE\RISE.EXE.lnk"
  ${EndIf}
  ClearErrors
  CreateShortCut "$DesktopLink" "$INSTDIR\RISE.exe" "" "$INSTDIR\RISE.exe" 0
  CreateShortCut "$StartLink" "$INSTDIR\RISE.exe" "" "$INSTDIR\RISE.exe" 0
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "$RegistryKey" "DisplayName" "RISE.EXE"
  WriteRegStr HKCU "$RegistryKey" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "$RegistryKey" "Publisher" "RISE contributors"
  WriteRegStr HKCU "$RegistryKey" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "$RegistryKey" "DisplayIcon" "$INSTDIR\RISE.exe,0"
  WriteRegStr HKCU "$RegistryKey" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "$RegistryKey" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "$RegistryKey" "NoModify" 1
  WriteRegDWORD HKCU "$RegistryKey" "NoRepair" 1
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "Setup could not create a shortcut or register the app."
    SetErrorLevel 1
    Abort
  ${EndIf}
SectionEnd

Function un.onInit
  SetShellVarContext current
  StrCpy $TestMode "0"
  ReadINIStr $0 "$INSTDIR\installer-test.ini" "Test" "Enabled"
  ${If} $0 == "1"
    StrCpy $TestMode "1"
  ${EndIf}
FunctionEnd

Section "Uninstall"
  SetShellVarContext current
  ${If} $TestMode == "1"
    StrCpy $RegistryKey "Software\RISE\InstallerTest"
    Delete "$INSTDIR\TestShortcuts\RISE.EXE.lnk"
    Delete "$INSTDIR\TestShortcuts\Start-RISE.EXE.lnk"
    RMDir "$INSTDIR\TestShortcuts"
    Delete "$INSTDIR\installer-test.ini"
  ${Else}
    ; The helper takes the app's normal single-instance lock. It refuses cleanup
    ; if RISE is open, and only removes this user's own RISE wake-task folder.
    ClearErrors
    ExecWait '$\"$INSTDIR\RISE.exe$\" --remove-wake-tasks' $0
    ${If} ${Errors}
      MessageBox MB_ICONSTOP "Could not remove RISE wake tasks. Keep the app installed and try again."
      SetErrorLevel 1
      Abort
    ${EndIf}
    ${If} $0 != 0
      MessageBox MB_ICONSTOP "Close RISE.EXE before uninstalling. If an alarm is ringing, complete its puzzle first."
      SetErrorLevel 1
      Abort
    ${EndIf}
    StrCpy $RegistryKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\RISE"
    Delete "$DESKTOP\RISE.EXE.lnk"
    Delete "$SMPROGRAMS\RISE.EXE\RISE.EXE.lnk"
    RMDir "$SMPROGRAMS\RISE.EXE"
  ${EndIf}
  ; Remove only installed files. Saved alarms under LOCALAPPDATA\RISE stay intact.
  Delete "$INSTDIR\RISE.exe"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\THIRD_PARTY_NOTICES.md"
  Delete "$INSTDIR\SHA256SUMS.txt"
  Delete "$INSTDIR\licenses\DOTNET-LICENSE.txt"
  Delete "$INSTDIR\licenses\DOTNET-THIRD-PARTY-NOTICES.txt"
  Delete "$INSTDIR\licenses\WPF-LICENSE.txt"
  Delete "$INSTDIR\licenses\NSIS-LICENSE.txt"
  RMDir "$INSTDIR\licenses"
  DeleteRegKey HKCU "$RegistryKey"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
