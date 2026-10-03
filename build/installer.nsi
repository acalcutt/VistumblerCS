; Windows installer for a self-contained .NET app. Built by Build-Installer.ps1, which passes these defines:
;   APP_NAME         Product name; also the install folder, shortcut and Apps & Features name
;   APP_EXE          Main executable, relative to the install folder
;   VERSION          Display version, e.g. 0.4.6 or 0.5.0-rc.1
;   VERSION_NUMERIC  Four-part numeric version for the file properties, e.g. 0.4.6.0
;   ARCH             x64 or arm64
;   PUBLISHER        Publisher name
;   FILE_LIST        Generated .nsh with the InstallFiles / UninstallFiles macros
;   OUTFILE          Installer path to write
;   APP_ICON         Optional .ico for the installer and uninstaller
;
; The uninstaller removes only the files this installer put down (from FILE_LIST), never the whole folder, so an
; unusual install folder can't take other files with it. Installing over an older version runs the old version's
; uninstaller first, so files dropped between versions don't pile up.
;
; In-app updates run this installer as: setup.exe /S /UPDATE /D=<install folder>. With /UPDATE it waits for the app to
; exit before replacing its files, and starts the app again when the install finishes.

Unicode true
ManifestDPIAware true

!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!macro RequireDefine NAME
  !ifndef ${NAME}
    !error "${NAME} is not defined: build this installer with Build-Installer.ps1"
  !endif
!macroend
!insertmacro RequireDefine APP_NAME
!insertmacro RequireDefine APP_EXE
!insertmacro RequireDefine VERSION
!insertmacro RequireDefine VERSION_NUMERIC
!insertmacro RequireDefine ARCH
!insertmacro RequireDefine PUBLISHER
!insertmacro RequireDefine FILE_LIST
!insertmacro RequireDefine OUTFILE

!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"

Name "${APP_NAME} ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\${APP_NAME}"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
BrandingText "${APP_NAME} ${VERSION} (${ARCH})"

VIProductVersion "${VERSION_NUMERIC}"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileVersion" "${VERSION_NUMERIC}"
VIAddVersionKey "FileDescription" "${APP_NAME} installer (${ARCH})"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "LegalCopyright" "${PUBLISHER}"

!ifdef APP_ICON
  !define MUI_ICON "${APP_ICON}"
  !define MUI_UNICON "${APP_ICON}"
!endif
!define MUI_ABORTWARNING
; Start the app through Explorer so it runs as the signed-in user, not elevated like the installer
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Start ${APP_NAME}"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApp

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

!include "${FILE_LIST}"

Function .onInit
  SetRegView 64
  SetShellVarContext all
  !if "${ARCH}" == "arm64"
    ${IfNot} ${IsNativeARM64}
      MessageBox MB_ICONSTOP "This installer is for Windows on ARM64 devices. Download the x64 installer for this PC." /SD IDOK
      Abort
    ${EndIf}
  !else
    ; x64 Windows, or Windows on ARM64, which runs x64 apps through emulation
    ${IfNot} ${IsNativeAMD64}
    ${AndIfNot} ${IsNativeARM64}
      MessageBox MB_ICONSTOP "${APP_NAME} needs 64-bit Windows." /SD IDOK
      Abort
    ${EndIf}
  !endif

  ; Upgrade in place. InstallDirRegKey can't do this: it reads the 32-bit registry view, and the install location is
  ; written to the 64-bit one. An explicit /D= other than the default still wins.
  ${If} $INSTDIR == "$PROGRAMFILES64\${APP_NAME}"
    ReadRegStr $0 HKLM "${UNINST_KEY}" "InstallLocation"
    ${If} $0 != ""
      StrCpy $INSTDIR $0
    ${EndIf}
  ${EndIf}

  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/UPDATE" $R1
  ${IfNot} ${Errors}
    Call WaitForAppExit
  ${EndIf}
FunctionEnd

; The app starts this installer and then exits; wait (up to 30 s) until its exe is no longer in use
Function WaitForAppExit
  StrCpy $R2 0
  ${DoWhile} ${FileExists} "$INSTDIR\${APP_EXE}"
    ClearErrors
    FileOpen $R3 "$INSTDIR\${APP_EXE}" a
    ${IfNot} ${Errors}
      FileClose $R3
      Return
    ${EndIf}
    ${If} $R2 >= 60
      MessageBox MB_ICONSTOP "${APP_NAME} is still running. Close it, then run this installer again." /SD IDOK
      Abort
    ${EndIf}
    IntOp $R2 $R2 + 1
    Sleep 500
  ${Loop}
FunctionEnd

Function .onInstSuccess
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/UPDATE" $R1
  ${IfNot} ${Errors}
    Call LaunchApp
  ${EndIf}
FunctionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext all
FunctionEnd

Function LaunchApp
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${APP_EXE}"'
FunctionEnd

Section "${APP_NAME} (required)" SecApp
  SectionIn RO

  ; Upgrade: let the installed version remove its own files first. /UPGRADE keeps its shortcuts and registration.
  IfFileExists "$INSTDIR\uninstall.exe" 0 +3
    ExecWait '"$INSTDIR\uninstall.exe" /S /UPGRADE _?=$INSTDIR'
    Delete "$INSTDIR\uninstall.exe"

  !insertmacro InstallFiles

  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\uninstall.exe"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

  WriteRegStr HKLM "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINST_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKLM "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKLM "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKLM "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINST_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "${UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd

Section /o "Desktop shortcut" SecDesktop
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
SectionEnd

Section "Uninstall"
  !insertmacro UninstallFiles
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"

  ; Run by a newer installer during an upgrade: leave the shortcuts and registration for it to update
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/UPGRADE" $R1
  ${If} ${Errors}
    Delete "$SMPROGRAMS\${APP_NAME}.lnk"
    Delete "$DESKTOP\${APP_NAME}.lnk"
    DeleteRegKey HKLM "${UNINST_KEY}"
  ${EndIf}
SectionEnd
