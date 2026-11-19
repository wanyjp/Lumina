# 贡献指南

感谢你愿意为拾光 Lumina 做贡献。提交代码前，请先阅读本指南与 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)。

## 1. 开发环境

- Windows 10 2004+ / Windows 11，64 位；
- .NET 10 SDK；
- 可选：Inno Setup 7，用于构建安装包；
- PowerShell 5.1 或 PowerShell 7。

```powershell
git clone https://github.com/wanyjp/Lumina.git
cd Lumina
dotnet restore Lumina.sln
dotnet build Lumina.sln -c Release
dotnet test Lumina.Tests\Lumina.Tests.csproj -c Release
```

完整门禁：

```powershell
pwsh .\scripts\verify.ps1
```

## 2. 分支与提交

- 从 `main` 创建分支：`feature/<简短描述>`、`fix/<简短描述>`、`docs/<简短描述>`；
- 提交信息说明“为什么改”，不要只写“修改”；
- 一个 PR 聚焦一个主题，避免混入无关重构；
- 不提交 `bin/`、`obj/`、`.artifacts/`、数据库、日志、音频资源或本地配置。

## 3. 代码要求

- 遵循 `.editorconfig`；保持 `Nullable` 与 `ImplicitUsings` 开启；
- 业务层不得直接写 SQL、不得直接调用 Win32/WinRT；系统能力放入 `Lumina.SystemAdapters`，表现层只依赖服务接口；
- 新增用户可见行为时，同步更新设置项、快捷键表与相关文档；
- 用户界面不得出现开发阶段编号或“待接入/占位”措辞；
- 不记录密码、主密码、剪贴板正文等敏感内容到日志。

## 4. 测试要求

- 行为变更必须附带或更新测试；
- 提交前执行 `pwsh .\scripts\verify.ps1`，确保构建 0 警告 0 错误、全量测试通过、格式与分层检查通过；
- 修复缺陷时优先补一个能复现问题的测试。

## 5. Pull Request 检查清单

- [ ] 从最新 `main` 创建分支；
- [ ] 构建通过，0 警告 0 错误；
- [ ] 全量测试通过；
- [ ] `dotnet format Lumina.sln --verify-no-changes` 通过；
- [ ] `pwsh .\scripts\check-layers.ps1` 通过；
- [ ] 未提交密钥、个人路径、用户数据或大体积二进制；
- [ ] 用户可见改动已更新 README/CHANGELOG 或相关文档。

## 6. 许可证

本项目源代码采用 [MIT License](LICENSE)。你提交的贡献将按同一许可证授权；请勿提交你无权授权的第三方代码或素材。第三方素材需在 PR 中说明来源与许可，并更新 `THIRD-PARTY-NOTICES.md`。
