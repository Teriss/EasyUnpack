# EasyUnpack

“教我解压千恋万花！”  
“为什么下载下来是个视频？”  
“这个文件连扩展名都没有，是要我靠意念解压吗？”

如果你也厌烦了下载资源后先猜格式、再改后缀、然后找密码，最后打开 `A/B/C/真正的文件` 这一套仪式感十足的流程，EasyUnpack 就是来替你跑腿的。

它是一个面向 Windows 10/11 的右键自动解压工具：选中文件或文件夹，右键一级菜单点一下，剩下的交给它。压缩包伪装成 `.jpg`、`.mp4`，甚至没有扩展名？可以。分卷压缩、嵌套压缩、每层密码不一样？尽量可以。解压结果套了五层目录？会自动折叠多余的单目录层级。

## 1.0.1 能做什么

- 通过 Explorer 一级右键菜单启动，不需要先寻找“那个不知道装在哪里的 exe”。
- 根据文件签名识别 ZIP、RAR、7z 等格式，不迷信扩展名；也能定位嵌在视频尾部附近的完整 ZIP 负载。
- 支持无扩展名、伪装扩展名和分卷压缩；分卷名称被改乱了也会尝试建立临时规范名称。
- 自动处理嵌套压缩包，直到找到实际内容或达到安全深度上限。
- 使用密码库按优先级自动尝试密码；密码成功后自动置顶。
- 密码库支持新增、编辑、删除、遮罩查看和拖拽调整顺序，可选主密码 AES-GCM 加密。
- 解压完成后显示实际输出目录；源文件只有在发布成功后才会尝试移入回收站。
- 自动折叠“目录里只有一个目录”的冗余路径，例如 `A/B/C/file.txt` 变成 `压缩包名/file.txt`。
- 使用 .NET 10 内置 Fluent 主题，跟随 Windows 浅色/深色模式。

## 怎么用

### 安装

从 [Releases](https://github.com/Teriss/EasyUnpack/releases) 下载 `EasyUnpack-Setup.exe`，以管理员身份安装。安装程序会注册文件和文件夹的一级右键菜单：

`使用 EasyUnpack 自动解压`

安装后不需要记住程序目录，也不需要把压缩包拖到某个神秘快捷方式上。

### 解压

1. 在资源管理器中选中一个或多个压缩包、伪装文件、分卷文件，或包含它们的文件夹。
2. 右键选择“使用 EasyUnpack 自动解压”。
3. 等待任务完成；任务列表会显示格式、源文件、输出目录和状态。
4. 遇到密码时，先自动尝试密码库；都失败后输入一次，成功密码会按设置保存。

如果你选中的是文件夹，EasyUnpack 会扫描其中可识别的压缩内容，不会擅自读取普通文件内容，也不会在 Explorer 进程里执行解压。

## 解压引擎

EasyUnpack 通过适配器调用外部工具。当前可用于自动解压的引擎包括：

- 7-Zip / NanaZip
- WinRAR
- Bandizip

PeaZip、WinZip、HaoZip 和 360 压缩可以被发现并显示，但只有存在完整、经过验证的命令行适配器时才会自动选择。没有检测到可用工具时，打开“引擎设置”手动选择可执行文件，或把工具加入 `PATH`。

## 密码库与安全性

密码列表只显示遮罩值；点击显示按钮才会在编辑区临时查看明文。密码不会写入任务状态、日志或截图。设置主密码后，密码库使用 PBKDF2-SHA256 派生密钥和 AES-256-GCM 加密。

密码库默认位于：

`%AppData%\EasyUnpack\passwords.json`

解压任务开始时会取得密码候选快照，因此正在进行的任务不会被后来打开的密码库窗口“半路改剧本”。

## 常见问题

### 右键菜单没有出现

请确认安装程序以管理员身份运行，并重新启动资源管理器。也可以重新运行安装包覆盖安装。安装器需要 x64 Windows。

### 提示没有可用引擎

安装 [7-Zip](https://www.7-zip.org/)、WinRAR 或 Bandizip，然后在“引擎设置”中重新扫描。也可以直接浏览选择对应的 `.exe`。

### 密码每层都不一样

在提示窗口逐层输入即可。成功密码会进入密码库；打开“密码库”可以编辑备注、删除错误密码或拖拽调整优先级。

### 解压后仍然有目录

只有“当前目录没有文件且只有一个普通子目录”的层级会被折叠。存在多个并列目录、文件与目录混合，或目录链接时会保留，因为那通常已经是有意义的结构，不该被软件自作聪明地揉成一团。

## 从源码构建

需要 .NET 10 SDK。运行测试和构建：

```powershell
dotnet test EasyUnpack.slnx
dotnet build EasyUnpack.slnx --configuration Release
```

构建 Windows 安装包还需要 Visual C++ Build Tools、Windows SDK 和 Inno Setup 6：

```powershell
.\tools\build-installer.ps1 -Configuration Release
```

输出文件位于 `artifacts\installer\EasyUnpack-Setup.exe`。完整构建说明见 [doc/build.md](doc/build.md)，架构决策见 [doc/architecture](doc/architecture)。

自动化 UI 测试可以设置 `EASYUNPACK_DATA_DIRECTORY`，把测试密码库与日常 `%AppData%\EasyUnpack` 隔离；普通使用不需要设置它。

## 项目状态

EasyUnpack 1.0.1 已完成核心自动解压、密码库、嵌套处理、媒体文件内嵌 ZIP 识别和 Windows 右键集成。它的目标不是替你研究压缩软件的全部玄学，而是把“下载完资源后的第一百零一步”变成一次右键点击。

欢迎提交 Issue 或 Pull Request：

[Teriss/EasyUnpack](https://github.com/Teriss/EasyUnpack)

## 许可证

本项目采用 [MIT License](LICENSE)。你可以自由使用、修改、商用和再分发，但请保留版权声明和许可证文本。
