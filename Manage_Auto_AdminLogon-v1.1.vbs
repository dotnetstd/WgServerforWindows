' 检查是否以管理员身份运行
If Not WScript.Arguments.Named.Exists("elevate") Then
    CreateObject("Shell.Application").ShellExecute "wscript.exe", """" & WScript.ScriptFullName & """ /elevate", "", "runas", 1
    WScript.Quit
End If

' 显示提示信息
MsgBox "注意：此操作仅适用于本地账户登录用户" & vbCrLf & _
       "若使用Microsoft账户登录，请先切换为本地账户" & vbCrLf & vbCrLf & _
       "作者：哔哩哔哩UP主 御坂07833号", vbInformation, "提示"

' 显示菜单
Dim choice
choice = MsgBox("请选择操作：" & vbCrLf & _
                "   " & vbCrLf & _
                "       是 - 开启自动登录" & vbCrLf & _
                "       否 - 关闭自动登录" & vbCrLf & _
                "       取消 - 退出", vbYesNoCancel + vbInformation, "选择操作")

' 处理用户选择
Select Case choice
    Case vbYes
        ' 获取用户名和密码
        Dim USERNAME, PASSWORD
        USERNAME = InputBox("请输入要自动登录的本地用户名：", "输入用户名")
        PASSWORD = InputBox("请输入对应的密码：", "输入密码")
        
        ' 开启自动登录
        CreateObject("WScript.Shell").RegWrite "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\AutoAdminLogon", "1", "REG_SZ"
        CreateObject("WScript.Shell").RegWrite "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\DefaultUserName", USERNAME, "REG_SZ"
        CreateObject("WScript.Shell").RegWrite "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\DefaultPassword", PASSWORD, "REG_SZ"
        MsgBox "自动登录已开启，请重启计算机以生效。", vbInformation, "提示"
    Case vbNo
        ' 关闭自动登录
        CreateObject("WScript.Shell").RegWrite "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\AutoAdminLogon", "0", "REG_SZ"
        MsgBox "自动登录已关闭，请重启计算机以生效。", vbInformation, "提示"
    Case vbCancel
        ' 退出程序
        MsgBox "退出程序。", vbInformation, "提示"
        WScript.Quit
End Select

' 显示作者信息
MsgBox "作者：哔哩哔哩UP主 御坂07833号", vbInformation, "作者信息"
