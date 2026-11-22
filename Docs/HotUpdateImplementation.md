# 资源与代码热更新实施记录

状态：实施中。运行时代码和编辑器脚本通过本机 .NET 静态编译（0 个错误）；Unity 已导入配置、完成资源构建和独立 Windows 客户端构建。使用者确认同一客户端在线、离线及内容更新正常，回退、双客户端版本拒绝和失败重试也已验证；CI 产物尚未完成验收。

## 已确认边界

- 平台：Windows，Unity `6000.3.10f1`，Addressables `2.9.1` 由使用者在 Unity 中安装。
- `MainMenu` 是客户端首场景，随包保留；`MainScene` 和其运行依赖是远程更新目标。`Assets/Editor` 和未被运行场景使用的素材不发布。
- 本机 HTTP 服务由使用者管理，以项目 `ServerData/` 为网站根目录，仅监听 `127.0.0.1:8000`。
- 首次安装后允许联网下载；首次成功下载后要能离线使用已有内容。
- CI 只生成待审核资源包，不自动更新网站根目录的 `latest.json`。正式发布由使用者审核后操作。
- 使用者在 Unity 中执行本项目配置脚本；助手不启动或配置 Unity 编辑器及 HTTP 服务。

## 迁移前可恢复基线

- Git 提交：`20d41a9a2580c36509b5daa49434f862210c1950`。
- 原有本机 `Build/rpg.exe`：SHA-256 `BF75364D79722A6DCDFEA55A2BC8B2AA6C43FB803A9C8D904751A6D13184E622`。`Build/` 被 Git 忽略，恢复这个二进制文件需要保留单独副本。
- `Docs/HotUpdateAndCIPlan.md` 在第四步开始前已有使用者未提交修改，本阶段不覆盖它。

## 资源清单与加载入口

- `Tools/resource_inventory.py` 生成 `Docs/HotUpdateResourceInventory.csv`，列出项目文件及两个场景的静态 GUID 可达性。当前有 1045 个资源文件：216 个项目 C# 文件、19 个 Editor C# 文件、16 个 Addressables 编辑器配置、242 个仅游戏场景可达、2 个仅菜单可达、39 个两场景共用、511 个未从两个场景的序列化引用可达。
- Unity 配置脚本将使用 `AssetDatabase.GetDependencies(MainScene, true)` 复核实际导入依赖。静态清单不能证明动态加载或导入器子资源的完整性；34 个未解析 GUID 包含 Unity 或包内引用，需结合 Unity 构建报告核对。
- 2026-11-21 首次运行配置脚本后，Unity 报告 279 个远程运行资源条目，2 个 TextMesh Pro `Resources` 字体依赖保留在客户端。Unity 曾提示原 Build Scenes 中的 `MainScene` 已被标为 Addressable 并自动禁用；最终 Build Scenes 文件仅含启用的 `MainMenu`。
- 当前 Addressables 设置生成二进制 Catalog（`.bin`）。发布清单脚本和客户端按实际扩展名处理 `.bin` 或 `.json`，不要求切换为 JSON Catalog。
- 首次审核构建 `20261121T130816Z`：`ServerData/releases/<releaseId>/StandaloneWindows64/` 有 1 个 Catalog、1 个 hash 和 8 个 Bundle，总计 21,406,064 字节；`Build/ResourceRelease/<releaseId>/` 有 `latest.json`、10 个文件的 SHA-256 记录、内容状态副本及构建布局报告。Unity 实际内容状态路径为 `Assets/AddressableAssetsData/Windows/addressables_content_state.bin`。构建报告 `BuildError` 为空，列出 3 个跨 Bundle 重复对象，其中 2 个 GUID 对应本地 TextMesh Pro 字体资源；还需在 Windows 客户端检查画面和内存。使用者审核同意后，已将 `latest.json` 复制到 `ServerData/` 根目录并核对 SHA-256。
- 使用者已启动本机 HTTP 服务；`GET /latest.json` 和 Catalog 均返回 200，下载的 Catalog SHA-256 与清单一致。使用者确认 Editor 的远程测试模式可进入 MainScene。`Build/HotUpdateTest/rpg.exe` 构建成功，其 `rpg_Data/StreamingAssets/aa` 包含本地初始化设置和 Catalog；使用者确认该客户端在线、停止 HTTP 后离线均可从 MainMenu 进入 MainScene。离线日志记录到更新检查失败，但游戏继续使用已保存版本。由于 Editor 远程测试先使用了同一应用的持久化目录，本次 Windows 启动不能单独证明全新用户首次下载，需要在新版本更新测试或隔离缓存测试中补齐。
- 审核构建脚本现在显式开启 Build Layout，并将 `buildlayout.json` 连同哈希放入审核产物。Windows 客户端 CI 构建前也会先构建 Addressables 内容，避免干净检出时缺少本地 Catalog。
- 当前 C# 运行时代码未发现 `Resources.Load`；物品数据库的 `AssetDatabase` 使用在 `UNITY_EDITOR` 条件内。`TextMesh Pro/Resources` 等 Resources 内容保持随包，并在构建布局中核对。
- MainMenu 和 MainScene 共用的音频、字体、UI 等依赖可能分别出现在随包场景和远程资源中；这是菜单首屏离线可用带来的重复，需要用 Addressables Build Layout 核对具体大小，并确认游戏场景确实加载远程版本。
- A/B 联机握手现在检查资源版本一致性；协议 SchemaVersion 从 `61` 提升到 `62`。不同资源版本应在进入房间前被拒绝，旧协议客户端也需要重新发布的基础客户端。
- 客户端资源清单同时核对 `Application.version` 和协议版本；基础客户端发生不兼容修改时必须提升客户端版本，并用该版本重新构建资源。
- 菜单在下载成功后提供 `Rollback resources`，可切到本机保存的上一版目录与缓存。回退后会屏蔽当前 `latest.json` 所指的版本，直到服务器发布不同的版本。

## 使用者在 Unity 中的操作

1. Unity Player Settings Version 已设为 `1.1.0`，Standalone Scripting Backend 已设为 IL2CPP。当前资源清单要求 `scriptAssemblyVersion=2`；版本 `20261122T104506Z` 是该客户端的全量基础资源，`20261122T114153Z` 是沿用同一 Catalog 的代码更新。
2. 退出 Play Mode，执行 `Tools > RPG Resource Update > 1 Configure Remote Content`。脚本保留 MainMenu 为唯一 Player Build 场景，建立远程 Profile 和资源组；检查 Addressables Groups 和 Build Settings。
3. 本次脚本程序集布局迁移必须执行 `Tools > RPG Resource Update > 2 Build Initial Review Content`，重新生成完整资源包；不要用旧版本的 `addressables_content_state.bin` 执行菜单 `3 Build Update Review Content`。Console 会显示新 `releaseId`。以后在同一客户端布局内做资源更新，才使用与对应发布版本匹配的内容状态。
4. 先在 Unity 的 HybridCLR 菜单生成 Windows 64 位热更 DLL 和所需 AOT 元数据，再运行 `python Tools/package_resource_release.py --release-id <Console 中的 releaseId>`。脚本默认读取 `Library/HybridCLR/HotUpdateDlls/StandaloneWindows64/Rpg.HotUpdate.dll`，并按 `Assets/HybridCLR/Generate/AOTGenericReferences.cs` 的 `PatchedAOTAssemblyList` 读取剥离后的 AOT DLL；它将这些文件复制到同一发布目录，并在待审核的 `latest.json` 中记录路径、字节数和 SHA-256。
5. 审核 `ServerData/releases/<releaseId>/` 与校验记录后，由使用者启动本机 HTTP 静态服务并将待审核的 `latest.json` 放到 `ServerData/` 根目录。这一步才让客户端看到新版本。日常 push 不执行这一步。
6. Editor 默认使用 Asset Database 直接加载 MainScene；需要在 Editor 验证远程资源时，执行 `Tools > RPG Resource Update > Toggle Remote Test In Editor`，并在 Console 确认已开启。实际下载、缓存和回退最终仍需 Windows 客户端验证。

## 代码热更加载器首版

- `ResourceRelease` 的 `scriptAssemblyVersion` 已升至 2，旧资源清单不再兼容。新清单必须包含 `hotUpdateDll` 和 `aotMetadata`（没有补充元数据时用空数组）。资源 Catalog 与代码文件必须使用同一个 releaseId。
- 客户端先校验并缓存 DLL 与元数据，调用 HybridCLR 补充 AOT 元数据，再 `Assembly.Load` 并反射调用 `MenuRuntimeEntry.Register()`。只有代码注册及资源准备都成功，菜单才进入 Ready 状态。编辑器直接使用已编译的 `Rpg.HotUpdate`，不重复加载 DLL。
- 回退前核验旧代码缓存及 Catalog 并下载旧资源；由于进程内不能替换已加载程序集，回退在重启客户端后生效。首次 Windows IL2CPP 客户端、AOT 元数据清单和同一客户端替换 DLL 的实际运行仍待使用者在 Unity 中验证。
- 仅修改热更代码时，在 Unity 执行 `HybridCLR > CompileDll > Win64`，然后用 `python Tools/package_code_release.py --base-release-id <当前版本> --release-id <新版本>` 复用原 Catalog，并**从已发布的基础版本复制**对应 AOT 元数据，不读取本机后来重新生成的 AOT 文件。保留基础版本的资源目录，因为 Catalog 仍引用其中的 Bundle。此流程不重打 Windows 客户端；若新增 AOT 泛型、桥接或基础层接口，需要先评估并构建新基础客户端。

### 同一客户端替换 DLL 验收（2026-11-22）

- 使用者构建 `Build/newBuild/rpg.exe`（Windows 64 位 IL2CPP）并确认可进入游戏。基础发布版本 `20261122T104506Z` 的 DLL SHA-256 为 `16c756f72a81c639da0fad2209e40857525e0ecb3f024dd9049deb7c267ed193`。
- 仅在 `MenuRuntimeEntry.Register()` 中加入 Player.log 标记，使用者执行 `HybridCLR > CompileDll > Win64`；代码版本 `20261122T114153Z` 复用完全相同的资源 Catalog，DLL SHA-256 变为 `83af751d8d04b218343d7bf7aeae4c4f1ccd1ac0d88fa388de282e8dec162493`。发布清单后，使用者重新启动原客户端。
- `Player.log` 记录 `[HotUpdate] DLL update probe v2 registered`，本地活动清单和缓存 DLL 的哈希均指向新版本。`rpg.exe` SHA-256 始终为 `C60CBB59C0E1E20DBE9BFFE0193A8E17E7065514A04745E9A1E5D08F1A12C745`，`GameAssembly.dll` 始终为 `DF9C6917CD113AD1C5DBE959B433489A271F23C71F1543A820212F22DA792226`。同一 IL2CPP 客户端替换 DLL 的基本链路已验证；损坏 DLL 恢复和代码版本回退仍需单独验证。
- 后续代码版本 `20261122T124206Z` 在热更入口显式重置两处静态状态；使用者重新编译 Win64 热更 DLL，沿用原 Catalog 与 6 个基础 AOT 文件发布，DLL SHA-256 为 `57c5ee25194329a2f1d78277ff75b6b0cf10d50264ca641f18b362282ee52872`。使用者确认原 Windows 客户端可进入游戏，`Player.log` 与本地清单指向新版本；故障和回退验收按使用者要求暂缓。

## 待验证的验收

- Unity 重新编译、资源组依赖与 Build Layout、Windows 基础客户端构建。
- 固定同一个旧客户端，只替换远程资源，验证菜单、场景、画面、动画、音频、战斗、背包、存档和 A/B 双客户端。
- 验证无更新、首次下载、断网后使用缓存、下载中断后重试、损坏目录拒绝加载和回退到上一资源版本。
- GitHub Actions `Resource review build` 仅手动运行并上传资源、版本清单、校验记录和内容状态；下载产物后重复旧客户端测试。

## 本机增量构建基线修正

- 首次已发布版本为 `20261121T130816Z`。第二次初始构建 `20261121T131032Z` 的 8 个 Bundle 与首次版本逐字节相同，Catalog 因 URL 不同而不同。
- 我在第二次构建后重打包首次版本，误把第二次构建的内容状态放进首次版本审核目录；首次版本原始状态已无法从现存文件恢复。误标状态已移除，第二次构建状态已保存到其自身目录，SHA-256 为 `F7784814F398DE2263301E20C9B4128BD8E733E287A647EB17A224514CF7484E`。后续内容更新以第二次构建状态为基线，并须保留其 `ServerData/releases/20261121T131032Z/` 文件，因为新版 Catalog 可能引用未变化的旧 Bundle。
- Addressables 内置增量构建要求远程 Catalog 加载地址与状态文件记录的地址一致。配置脚本现在为 Catalog 使用独立 Profile 变量；增量构建期间设置为基线地址，而 Bundle 仍使用新版目录。客户端使用 `latest.json` 明确加载新版 Catalog。
- 打包脚本拒绝以不同 Catalog、文件清单或内容状态覆盖已归档的同一 releaseId，并在审核记录中记录内容状态和构建布局的 SHA-256。
- Dodge 技能树图标在 `Assets/Scenes/MainScene.unity` 中改为同一图集未使用的第 178 号切片。增量构建 `20261121T133911Z` 成功，布局报告 `BuildError` 为空。与首次发布 `20261121T130816Z` 相比，新 Catalog、hash 和 MainScene Bundle 变化；其余 7 个 Bundle 内容相同。审核包记录基线 `20261121T131032Z` 与其状态 SHA-256。使用者审核同意后，已将新版 `latest.json` 发布到本机测试目录并核对哈希。
- 固定的 `Build/HotUpdateTest/rpg.exe` 未重建，使用者确认它下载新版后显示 Dodge 新图标；本地清单变为 `20261121T133911Z`，`.previous` 保留 `20261121T130816Z`。使用者点击 `Rollback resources` 后，本地清单恢复旧版并记录屏蔽新版。曾有两次 UDP 连接超时，未进入版本握手；重新建立旧版房间后，A/B 同版本可连接。随后保持 A 旧版运行，移走屏蔽标记让 B 加载新版，B 加入 `127.0.0.1:1111` 时被明确以资源版本不一致拒绝。屏蔽标记备份在 `Build/ResourceRelease/blocked-resource-version-test-backup.txt`。
- 失败重试验证时，用户关闭客户端与 HTTP 服务；我将 HotUpdate 目录的 4 个 Catalog/清单文件暂存到 `Build/HotUpdateRetryBackup`。空目录启动的客户端日志显示 `No usable game resources`，用户确认出现 Retry；启动 HTTP 后点击 Retry，可进入 MainScene，本地清单写入新版。客户端退出后我恢复了原 4 个文件，重试新生成的 2 个文件留在 `Build/HotUpdateRetryGenerated`。
- GitHub 手动资源审核构建首次运行 `44912579900`：Unity 内容构建成功，`Stage manifest and checksums` 失败，因打包步骤期望 `ci-44912579900-17eae3ac`，而 GameCI 未向 Unity 进程传递 `GITHUB_RUN_ID` / `GITHUB_SHA` 环境变量。已改用 GameCI `customParameters` 向 Unity 显式传入 `-rpgReleaseId`，并以完整提交 SHA 统一两个步骤的目录；增量构建的内容状态路径也通过命令行参数传入。待重新运行 CI 验证。
