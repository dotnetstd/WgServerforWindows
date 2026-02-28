@echo off
@chcp 936 > nul
:: 检查是否以管理员身份运行
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo 正在请求管理员权限...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo ================================
echo 注意：此操作仅适用于本地账户登录用户
echo 若使用Microsoft账户登录，请先切换为本地账户
echo ================================
echo 作者：哔哩哔哩UP主 御坂07833号
echo ================================
echo.

:MENU_LOOP
echo.
echo 请选择操作：
echo 1. 开启自动登录
echo 2. 关闭自动登录
echo 3. 退出
set /p choice=请输入你的选择（1/2/3）：
echo.

if "%choice%"=="1" goto ENABLE_AUTO_LOGON
if "%choice%"=="2" goto DISABLE_AUTO_LOGON
if "%choice%"=="3" exit
echo 输入无效，请重新输入
goto MENU_LOOP

:ENABLE_AUTO_LOGON
:: 输入验证循环
:INPUT_USERNAME
set /p USERNAME=请输入要自动登录的本地用户名：
if "%USERNAME%"=="" (
    echo 错误：用户名不能为空
    goto INPUT_USERNAME
)

:INPUT_PASSWORD
set /p PASSWORD=请输入对应的密码：
if "%PASSWORD%"=="" (
    echo 错误：密码不能为空
    goto INPUT_PASSWORD
)

:: 注册表操作带错误检查
reg add "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon /t REG_SZ /d 1 /f || goto REG_ERROR
reg add "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultUserName /t REG_SZ /d "%USERNAME%" /f || goto REG_ERROR
reg add "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultPassword /t REG_SZ /d "%PASSWORD%" /f || goto REG_ERROR

echo.
echo [成功] 自动登录已开启，请重启计算机以生效。
goto END

:DISABLE_AUTO_LOGON
reg add "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon /t REG_SZ /d 0 /f || goto REG_ERROR
reg delete "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultPassword /f >nul 2>&1
reg delete "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultUserName /f >nul 2>&1

echo.
echo [成功] 自动登录已关闭，相关凭证信息已清除，请重启计算机以生效。
goto END

:REG_ERROR
echo.
echo [错误] 注册表操作失败，请检查权限后重试
goto END

:END
echo.
echo ================================
echo 作者：哔哩哔哩UP主 御坂07833号
echo ================================
pause
