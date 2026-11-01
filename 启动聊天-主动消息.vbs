Option Explicit
Dim fso, sh, base
Set fso = CreateObject("Scripting.FileSystemObject")
base = fso.GetParentFolderName(WScript.ScriptFullName)
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = base
' 主动消息现在由程序内置（每分钟检查一次），不再需要外部 主动消息.ps1
sh.Run """" & base & "\启动本地AI对话.exe""", 1, False