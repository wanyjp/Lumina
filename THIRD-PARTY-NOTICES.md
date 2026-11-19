# 第三方资源与许可

本项目源代码采用 [MIT License](LICENSE)。以下第三方资源按各自许可使用，不适用 MIT License。

## 1. NuGet 依赖（直接引用）

| 包 | 版本 | 许可 | 用途 |
| --- | --- | --- | --- |
| Microsoft.Data.Sqlite | 10.0.14 | MIT | 本地 SQLite 数据访问 |
| Microsoft.Extensions.DependencyInjection | 10.0.14 | MIT | 依赖注入 |
| Microsoft.Extensions.Logging | 10.0.14 | MIT | 结构化日志 |
| Microsoft.Testing.Platform | 2.5.2 | MIT | 测试运行平台 |
| NAudio | 2.2.1 | MIT | 音频播放与设备管理 |
| NAudio.Vorbis | 1.5.0 | MIT | OGG Vorbis 解码 |
| Vortice.Direct3D11 | 3.8.3 | MIT | Direct3D 11 互操作 |
| xunit.v3 | 4.0.2 | Apache-2.0 | 自动化测试框架 |

传递依赖（如 `SQLitePCLRaw.*`、`Microsoft.Extensions.*`、`System.*` 等）遵循其上游许可。完整依赖图可通过以下命令查看：

```powershell
dotnet list Lumina.sln package --include-transitive
```

## 2. 白噪音音频

白噪音资源包共 62 个文件，约 1.16 GB，**不纳入 Git 仓库**，随官方安装包或 GitHub Release 资源包分发。

- 授权类型：CC0 1.0、CC BY 3.0、CC BY 4.0 等；
- 作者、原始出处、许可与文件规格：见 [THIRD-PARTY-AUDIO.md](THIRD-PARTY-AUDIO.md)；
- CC BY 素材要求保留作者与来源署名，分发时不得删除本清单；
- 音频素材不适用 MIT License，单独提取或再分发时须遵守其原始许可。

## 3. 提示音

- 公开源码树中的 `Lumina.App/Sounds/notify-1.wav`、`notify-2.wav` 为项目原创合成音，按 CC0 1.0 释出，可自由使用；
- 1.0.0 官方安装包曾使用来自 [Tukuppt](https://www.tukuppt.com/) 的提示音素材；该素材不适用 MIT License，也不包含在公开源码树中。再分发官方安装包时，请同时遵守 Tukuppt 的授权条款。

## 4. 品牌资源

- `Lumina.App/Assets/lumina-logo.png`、`Lumina.App/Assets/lumina.ico` 以及“拾光 Lumina”名称、标识与界面文案为项目品牌资源；
- 品牌资源不适用 MIT License，未经书面许可不得以官方名义发布修改版或暗示官方背书。

## 5. 字体与图标

- 界面使用 Windows 系统字体（如 Segoe UI）与 Segoe MDL2 Assets 图标字体；
- 项目不随附或再分发上述字体文件；
- 如未来引入第三方字体或图标包，应在此补充许可与来源。

## 6. 构建与发布工具

- Inno Setup 7：用于生成 Windows 安装包，按其自身许可使用；
- GitHub Actions：CI 构建与校验；
- 以上工具不随应用二进制分发。

## 7. 许可冲突

如本文件与第三方原始许可存在冲突，以第三方原始许可为准。发现遗漏或错误时，请提交 Issue 或 PR 更正。
