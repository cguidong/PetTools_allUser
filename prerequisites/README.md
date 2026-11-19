# Microsoft Visual C++ x64 运行库

此目录不提交安装程序二进制文件。普通源码构建不需要该文件；生成完整安装包时需要。

从微软官方渠道下载 x64 v14 运行库，并保存为本目录的 `VC_redist.x64.exe`：

- 官方说明：https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist
- 官方下载：https://aka.ms/vc14/vc_redist.x64.exe

安装包脚本要求至少 14.51.36247.0，并验证 Microsoft Corporation 的有效数字签名。请勿从非官方站点获取或改名替代其他架构的安装程序。

该文件被 `.gitignore` 排除。准备后重新发布即可随安装包内嵌；用户首次安装时可能需要管理员授权和稍后重启。