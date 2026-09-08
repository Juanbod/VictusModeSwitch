Option Explicit

Dim shell, fileSystem, brokerPath, powershellPath, commandLine, exitCode, quote

Set shell = CreateObject("WScript.Shell")
Set fileSystem = CreateObject("Scripting.FileSystemObject")

brokerPath = fileSystem.BuildPath( _
    fileSystem.GetParentFolderName(WScript.ScriptFullName), _
    "BiosBroker.ps1")
powershellPath = shell.ExpandEnvironmentStrings("%SystemRoot%") & _
    "\System32\WindowsPowerShell\v1.0\powershell.exe"
quote = Chr(34)
commandLine = quote & powershellPath & quote & _
    " -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden" & _
    " -ExecutionPolicy Bypass -File " & quote & brokerPath & quote

exitCode = shell.Run(commandLine, 0, True)
WScript.Quit exitCode
