
<img src="https://user-images.githubusercontent.com/7417301/170061921-fee5feb8-b348-40a8-9cd4-8db46d1aceec.png" />

# Wg Server for Windows (WS4W)
WS4W 是一个桌面应用程序，允许在 Windows 上运行和管理 WireGuard 服务端。

> **新功能**：现已支持 **中/英文** 双语切换！（包含全新 Modern UI 界面升级）

# WS4W v2.2.0 - 更新内容
* **双语支持**：可以从新的标题栏在英文和中文（简体）之间无缝切换。
* **现代 UI**：完全重新设计的界面，拥有更简洁、更现代的外观和感觉。
* **标题栏**：新的常驻标题栏，用于快速访问应用标题和语言设置。
* **样式改进**：所有窗口和控件的字体和样式保持一致。

受 Henry Chang 的文章 [How to Setup Wireguard VPN Server On Windows](https://www.henrychang.ca/how-to-setup-wireguard-vpn-server-on-windows/) 的启发，我的目标是创建一个能够自动化并简化许多复杂步骤的应用程序。虽然它还不是一个完全即插即用的解决方案，但其核心理念是能够逐一执行每个前提步骤，而无需运行任何脚本、修改注册表或进入控制面板。

# 开始使用
最新版本可在[此处](https://github.com/micahmo/WgServerforWindows/releases/latest)下载。下载安装程序并运行即可。

> **注意**：应用程序将请求以管理员身份运行。由于涉及注册表修改、Windows 服务、wg.exe 调用等，以提升权限运行整个应用程序会更方便。

#### 从 1.5.2 升级
在引入安装程序之前，WS4W 是以绿色版（便携式）应用程序分发的。绿色版本（1.5.2 及更早版本）没有到安装程序版本的自动升级路径。要升级，只需删除下载的绿色版本并下载安装程序即可。配置设置不会丢失。

# 它能做什么？

以下是可以使用此应用程序自动执行的任务。

## 初始状态

![BeforeScreenshot](https://user-images.githubusercontent.com/7417301/219172904-ff6d90d8-79a2-40c9-a038-3a5ad3386089.png)

### WireGuard.exe
此步骤从 https://download.wireguard.com/windows-client/wireguard-installer.exe 下载并运行最新版本的 WireGuard for Windows。安装后，也可以直接从 WS4W 中卸载。

### 服务器配置
![ServerConfiguration](https://user-images.githubusercontent.com/7417301/170072344-598a8b9c-bec8-4f34-9a85-ee95765520e3.png)

在这里你可以配置服务端。有关这些字段的具体含义，请参阅 WireGuard 文档。私钥（Private Key）和公钥（Public Key）分别通过调用 `wg genkey` 和 `wg pubkey [private key]` 生成。（你也可以选择提供自己的私钥。）

> **注意**：服务端的网络范围不能与主机系统的 IP 地址或局域网网络范围冲突，这一点非常重要。

除了为服务端创建/更新配置文件外，编辑服务器配置还会更新 `ScopeAddress` 注册表值（位于 `HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters`）。这是在使用 Internet 共享功能时，WireGuard 适配器所使用的 IP 地址。因此，服务器配置的 Address 属性决定了客户端的可分配地址，以及 Windows 在执行 Internet 共享时将分配给 WireGuard 适配器的 IP。请注意，IP 地址是在首次执行 Internet 共享时从 `ScopeAddress` 获取的。这意味着如果在配置中更改了服务器的 IP 地址（从而更新了 `ScopeAddress` 注册表值），WireGuard 接口将不再准确反映所需的服务器 IP。因此，WS4W 会提示重新共享 Internet。如果取消，Internet 共享将被禁用，必须手动重新启用。

> **重要提示**：你必须在路由器上配置端口转发。将所有指向服务端端口（默认为 `51820`）的 UDP 流量转发到服务器的局域网 IP。每个路由器都不同，因此很难给出具体的指导。例如，以下是在 Verizon Quantum Gateway 路由器上的端口转发规则。
> 
> ![](https://user-images.githubusercontent.com/7417301/127727564-0d666c41-4998-4c5d-8d2a-e7b730e545c8.png)

你应该将 Endpoint 属性设置为你的公网 IPv4、IPv6 或域名地址，后跟转发的端口。`Detect Public IP Address` 按钮将尝试使用 [ipify.org](https://ipify.org) API 自动检测你的公网地址。但是，如果可能，建议使用带有 DDNS 的域名。这样，如果你的公网 IP 地址发生变化，客户端无需重新配置即可找到你的服务器。

### 客户端配置
![ClientConfiguration](https://user-images.githubusercontent.com/7417301/218290866-f36b8bda-208f-4dfd-a66f-40e5b960b0af.png)

在这里你可以配置客户端。Address 可以手动输入，也可以根据服务器的网络范围计算。例如，如果服务器的网络是 `10.253.0.0/24`，客户端配置可以确定 `10.253.0.2` 是一个有效地址。请注意，范围内的第一个地址（在本例中为 `10.253.0.1`）保留给服务器。DNS 是可选的，但建议设置。你可以添加 DNS 搜索域（也称为 DNS 后缀）。最后，私钥、公钥和预共享密钥（Preshared Key）使用 `wg genkey`、`wg pubkey [private key]` 和 `wg genpsk` 生成。（你可以指定自己的私钥。预共享密钥是可选的，按客户端唯一生成，并与服务器配置共享。）

> 由于 WireGuard 的一个小特性，如果你删除客户端预共享密钥并同步服务器配置，WireGuard 仍然会期望客户端使用 PSK 连接。因此，WS4W 不允许你清除客户端的预共享密钥字段。取而代之的是，删除并重新创建客户端以移除 PSK。

配置完成后，可以通过二维码或导出 `.conf` 文件轻松将配置导入你选择的客户端应用。

![ClientQrCode](https://user-images.githubusercontent.com/7417301/170073360-628712b3-90e2-4ea5-a759-2dd6c9d5dc4a.png)

出于安全考虑，你可能不想在服务器上保留客户端的私钥。在这种情况下，你可以在保存客户端配置之前清除私钥字段。但是，有两点需要注意：
1. 你应该在移除私钥并保存之前导出客户端配置（通过二维码或文件）。
2. 如果你以后需要再次将配置导入客户端，你将不得不重新生成私钥和公钥。

### 隧道服务 (Tunnel Service)
服务端和客户端配置完成后，你可以安装隧道服务，它会使用 `wireguard /installtunnelservice` 命令为 WireGuard 创建一个新的网络接口。安装后，也可以直接在 WS4W 中移除隧道。这使用的是 `wireguard /uninstalltunnelservice` 命令。

完成此步骤后，WireGuard 客户端应该能够成功与服务器进行握手。

> **注意：** 如果在安装隧道服务后编辑服务器配置，隧道服务将通过 `wg syncconf` 命令自动更新（如果新保存的服务器配置有效）。客户端配置也是如此，客户端的更新通常会导致服务器配置的更新（例如，如果添加了新客户端，服务器配置必须意识到这个新对等点）。

### 专用网络 (Private Network)
即使安装了隧道服务，某些协议仍可能被阻止。建议将网络配置文件更改为“专用（Private）”，这会放宽 Windows 对网络的限制。

此步骤还会创建一个 Windows 任务，以便在启动时自动将网络设为专用。你可以通过下拉菜单禁用该任务。

> **注意**：在共享 Internet 连接源自域网络的系统上，此步骤不是必需的，因为 WireGuard 接口会继承共享域网络的配置文件。


### 路由 (Routing)

最后一步是允许通过 WireGuard 接口发出的请求被路由到你的专用网络或 Internet。为此，必须将 Windows 机器上“真实”网络适配器的连接共享给虚拟 WireGuard 适配器。这可以通过以下两种方式之一完成：
* NAT 路由
* Internet 共享 + 持久 Internet 共享

第一种方案仅在某些系统上可用（见下文）。第二种方案可以根据需要使用，但有一些局限性（例如，如果 Internet 连接共享给了 WireGuard 适配器，它就不能再共享给任何其他适配器）。还有多份关于 Internet 共享的问题报告，因此如果可用，应优先使用 NAT 路由。

这些选项是互斥的。

#### NAT 路由

在这里，你可以在 WireGuard 接口上创建 NAT 路由规则，以允许它与你的专用/公用网络交互。具体来说，会调用以下命令：

* 在 WireGuard 适配器上调用 `New-NetIPAddress`，以在服务器配置的 Address 属性范围内分配静态 IP。
* 调用 `New-NetNat` 在 WireGuard 适配器上创建新的 NAT 规则。
* 创建一个 Windows 任务以在启动时调用 `New-NetIPAddress`。
  * 如果你不希望 Windows 任务在启动时自动配置 WireGuard 接口，可以点击下拉菜单并选择“禁用自动 NAT 路由（Disable Automatic NAT Routing）”。

> NAT 路由至少需要 Windows 10，在旧版本的 Windows 上，启用它的选项甚至不会出现在应用程序中。然而，即使是 Windows 10，NAT 路由也并不总是有效。有时它需要启用 Hyper-V，应用程序会对此进行提示，但这还需要 Pro 或更高版本（即非家庭版）的 Windows。最终，如果应用程序无法启用 NAT 路由，它会建议改用 Internet 连接共享（见下文）。

#### Internet 共享
![InternetSharing](https://user-images.githubusercontent.com/7417301/170073850-fde3a685-79d5-4ea9-a2b6-acb9b08c58d0.png)

如果 NAT 路由不可用，你可以使用 Internet 共享为 WireGuard 接口提供网络连接。配置此选项时，你可以选择要共享的任何网络适配器。请注意，它可能仅对状态为“已连接”的适配器有效，并且仅对提供 Internet 或局域网访问的适配器有用。选择要共享的适配器时，将鼠标悬停在菜单项上可获取更多详细信息，包括适配器分配的 IP 地址。

> **注意：** 执行 Internet 共享时，WireGuard 适配器会从 `ScopeAddress` 注册表值（位于 `HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters`）中分配一个 IP。更新服务器配置的 Address 属性时会自动设置此值。

#### 持久 Internet 共享
Windows 中存在导致重启后 Internet 共享被禁用的问题。如果 WireGuard 服务器打算无人值守运行，建议启用持久 Internet 共享，这样重启后无需人工干预。

启用此功能时，会在 Windows 中执行两个操作：
1. `Internet Connection Sharing` 服务的启动模式从“手动”更改为“自动”。
2. 将 `HKLM\Software\Microsoft\Windows\CurrentVersion\SharedAccess` 中的 `EnableRebootPersistConnection` 注册表值设置为 `1`（如果未找到则创建）。

即使有了这些变通方法，Internet 共享仍可能在重启后被禁用。因此，还会执行一个操作：创建一个计划任务，在系统启动时使用 WS4W CLI 禁用并重新启用 Internet 共享。这应该足以保证共享保持启用状态。

### 查看服务器状态
![ServerStatus](https://user-images.githubusercontent.com/7417301/170075139-0647c35d-ac30-4296-93c1-985f1310c051.png)

安装隧道后，可以查看 WireGuard 接口的状态。这是通过 `wg show` 命令实现的。只要勾选了 `Update Live`，它就会持续更新。

### 设置

* 设置启动任务延迟 (Set Boot Task Delay)
  
  此设置允许为启动任务配置延迟。这对于依赖加载较慢的适配器的任务非常有用。请注意，更改此值后必须禁用并重新启用任务。

## 完成状态

![AfterScreenshot](https://user-images.githubusercontent.com/7417301/219172736-083417e2-1952-4e55-8988-06e75b44e33d.png)

## 命令行界面 (CLI)
绿色版下载中还捆绑了一个名为 `ws4w.exe` 的 CLI，可以从终端调用或从脚本中调用。除了写入标准输出的消息外，CLI 还会根据执行给定命令的成功情况设置退出代码。例如，在 PowerShell 中，可以使用 `echo $lastexitcode` 打印退出代码。

> **注意**：出于与上述相同的原因，CLI 也必须以管理员身份运行。

### 使用方法
CLI 使用动词（顶级命令），每个动词都有自己的一组选项。你可以运行 `ws4w.exe --help` 查看所有动词的列表，或运行 `ws4w.exe verb --help` 查看特定动词的选项列表。

#### 支持的动词列表
* ```ws4w.exe restartinternetsharing [--network <NETWORK_TO_SHARE>]```
	* 这将告诉 WS4W 尝试重新启动 Internet 共享功能。
	* 可以传递 `--network` 选项来指定 WS4W 应该共享哪个网络。
	* 如果已启用 Internet 共享，WS4W 将尝试重新共享相同的网络（除非传递了 `--network`）。
	* 如果已经共享了多个网络，则无法确定哪个网络与 WireGuard 网络共享，因此必须传递 `--network` 选项来指定。
	* 如果尚未启用 Internet 共享，则必须传递 `--network` 选项，否则无法知道要共享哪个网络。
	* 如果请求的或之前共享的网络成功重新共享，退出代码将为 0。
      > 此命令由启用持久 Internet 共享时创建的计划任务使用。
* ```ws4w.exe setpath```
    * 这将告诉 WS4W 将当前执行目录添加到系统的 `PATH` 环境变量中。
    * 此动词没有选项。
      > 此命令由安装程序在选择“将 CLI 添加到 PATH”选项时使用。
* ```ws4w.exe setnetipaddress --serverdatapath <PATH_TO_SERVER_CONFIG>```
    * 这将告诉 WS4W 在 WireGuard 接口上调用 `Set-NetIPAddress`，使用给定 WireGuard 服务器配置文件中定义的网络地址。
      > 此命令由启用 NAT 路由时创建的计划任务使用。
* ```ws4w.exe privatenetwork```
    * 这会将 WireGuard 网络接口的类别设置为专用（Private）。
    * 此动词没有选项。
      > 此命令由启用专用网络时创建的 Windows 任务使用。

# 已知问题

### 无法启用 Internet 共享

首先，建议优先使用 NAT 路由（如果可用）。

但是，如果在启用 Internet 共享时遇到以下错误消息，请执行以下手动步骤。

![image](https://user-images.githubusercontent.com/7417301/170076429-d08685ef-3eae-4433-978f-1adc722763c0.png)

 - 打开控制面板中的“网络连接”。
 - 右键点击你想要共享的网络接口 > 属性。
    - 转到“共享”选项卡，勾选“允许其他网络用户通过此计算机的 Internet 连接来连接”。
	- 从“家庭网络连接”下拉菜单中选择 `wg_server`。
	- 点击确定。
 - 关闭并重新打开 WS4W。现在它应该显示已启用 Internet 共享，后续的禁用/重新启用尝试应该都能成功。

> 注意：此问题通常在为虚拟机创建新的虚拟交换机后触发。手动解决方法应该只需执行一次，且不会影响虚拟交换机。

# 兼容性
WS4W 已经过测试，已知可在 Windows Server（2012 R2 及更新版本）和 Windows Desktop（10 及更新版本）上运行。

# 致谢

WireGuard 是 Jason A. Donenfeld 的注册商标。

[图标](https://www.flaticon.com/free-icon/sign_28310) 由 [Freepik](https://www.flaticon.com/authors/freepik) 制作，来源于 [www.flaticon.com](https://www.flaticon.com/)。
