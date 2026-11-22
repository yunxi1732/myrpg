# Windows 客户端：CI、资源热更新与代码热更新计划

状态：第 0～3 步已验收；第 4 步的资源迁移、更新机制、本机关键路径验证及 CI 审核构建已完成，CI 产物下载审核和完整游戏回归尚未验收。代码热更新第 5 步已完成程序集迁移、Windows IL2CPP 基础客户端、DLL 加载及同一客户端替换 DLL 的基础验证；CI 生成链和故障/完整游戏回归仍待验收。

## 已确认的范围

- 首版发布平台：Windows 客户端。
- 资源方案：Unity Addressables。全项目资源纳入统一清单；启动必需资源随客户端，其余适合更新的现有资源在资源阶段一起完成迁移与验收。
- C# 代码方案：HybridCLR。
- 当前工作方式：push 到 GitHub 作为备份；本机打包，使用客户端或 Unity Editor Play Mode 手动测试。
- 保留 Play Mode 和双客户端手动验收。push 触发自动检查；自动构建只产生测试产物，不自动发布给玩家。
- CI 运行位置：GitHub 托管 Windows 运行器（2026-11-21 确认）。远程资源存放位置和资源发布方式在资源阶段开始前确认。

## 执行规则

一次只推进一个实施阶段。阶段内部可以分多次提交和检查，但机制、现有内容迁移与最终验收属于同一交付，不把单个演示资源或规则当作阶段完成。若 Unity 编译、测试或实际客户端验证失败，先在当前阶段修复。修改现有资源引用、程序集边界、构建流程或协议之前，记录当前可用构建及恢复方式。每次发布记录客户端版本、资源版本、热更 DLL 版本、协议版本和对应 Git 提交。

## 阶段一：最小 CI

### 0. 选择 CI 运行位置并记录基线

**本步工作**：确认 GitHub 仓库可运行 Actions；检查实际 Unity 安装版本、Windows 构建目标、脚本后端、现有验证入口和可运行客户端。比较 GitHub 托管 Windows 运行器与自托管 Windows 运行器，只选一种作为首版 CI 环境。

**验收记录（2026-11-21）**：已选择 GitHub 托管 Windows 运行器。理由是无需另一台自有电脑持续在线，也不会让自动任务与本机 Unity 编辑器争用同一个项目目录。先前读取到 `ProjectVersion.txt` 为 Unity `6000.3.10f1`，项目包含 `Assets/Editor` 下的菜单验证脚本；这是静态检查结果。使用者提供的截图显示 GitHub 仓库 `yunxi1732/myrpg` 可打开 Actions 页面；使用者运行 `git remote get-url origin`，输出指向同一仓库。使用者确认现有 Windows 客户端可启动、进入游戏，Windows Player 脚本后端为 Mono，当前构建目标为 Windows。此步未切换脚本后端、未修改玩法、未创建 CI 工作流；HybridCLR 所需的 IL2CPP 构建另在第 5 步验收。

| 选择 | 优点 | 代价 |
| --- | --- | --- |
| GitHub 托管 Windows | 不用维护常开电脑；每次环境相对干净 | Unity 安装、授权和缓存要配置；导入与构建可能较慢；后续需验证 HybridCLR 工具链 |
| 自托管 Windows | 可复用已安装的 Unity、平台模块和缓存；更接近本机环境 | 电脑需在线；自己维护工具、授权和安全；不要与同一项目的编辑器操作争用工作目录 |

**验收**：文档写明最终选择及原因；记录一份当前可运行 Windows 构建、Unity 版本和手动验证结果。此步不修改玩法。

### 1. push 后自动检查 Unity 编译

**本步工作**：新增最小 GitHub Actions 工作流，在选定运行器上用项目锁定的 Unity 版本导入并编译项目；保存 Editor 日志，让编译失败显示为红色。先不自动构建客户端。

**授权路线（2026-11-21 已选）**：使用者的授权为 Unity Personal，决定在 GitHub 托管 Windows 运行器上尝试 GameCI Personal 授权流程。Unity 6000.3 官方文档要求 Personal 通过 Unity Hub 激活；GameCI 的第三方流程要求将本地 `.ulf` 文件内容、Unity 账户邮箱及密码分别保存为仓库的 `UNITY_LICENSE`、`UNITY_EMAIL`、`UNITY_PASSWORD` Actions Secrets。使用者确认本机 `C:\ProgramData\Unity\Unity_lic.ulf` 存在；CI 仅在主分支 push 时运行；首次 CI 验证时可暂时退出本机 Unity Hub。使用者暂未找到 GitHub 仓库的 `Secrets and variables` 页面，需确认页面入口或仓库设置权限。使用者自行操作 GitHub 和 Unity，助手只修改本地项目文件并提供步骤；不得要求使用者把授权文件、密码或令牌发到聊天。授权是否能在临时 Windows 运行器成功，须以首次实际工作流结果验收，不能预先标为通过。

**验收**：一次正常 `main` push 后，GitHub Actions 显示通过，日志出现 `[CI] Unity script compilation completed.`；工作流不依赖本机 `Library` 或 `Build` 目录。若自然发生编译错误，应显示失败并保留日志。临时分支故意制造错误的演练不属于本步，因为使用者只授权 `main` push 自动触发且工作流使用 Unity 授权 Secrets。

**验收记录（2026-11-21）**：使用者已自行配置三个 Actions Secrets。`.github/workflows/unity-compile.yml` 仅在 `main` push 时自动运行；GameCI 在 Windows 运行器调用 `Assets/Editor/CiCompileCheck.cs` 的静态入口，不构建客户端、不发布。使用者推送提交 `2a33bbd` 后提供截图，显示 `Unity compile #1` 在 `main` 上绿色成功、用时约 10 分 10 秒，并确认该次运行日志出现 `[CI] Unity script compilation completed.`。据此确认首次云端 Unity 导入、编译和入口调用通过；未演练故意编译错误的失败路径。助手未运行 GitHub 或 Unity。

### 2. 自动运行一个现有的确定性验证

**本步工作**：审查 `Assets/Editor` 中已有的菜单验证，选择一个无需人工点击、结果稳定的入口，封装为可在批处理模式运行且失败时返回非零退出码的检查。只接入这一个验证。

**验收**：`main` push 后 CI 成功，日志同时出现 `[PrimaryAttackRequests] PASS` 和 `[CI] Primary attack verification completed.`；保留该次 GitHub Actions 日志。使用者选择本步暂不演练故意失败路径，不将异常时返回非零的行为标为实测通过。后续每次只在有实际回归价值时增加一个检查。

**验收记录（2026-11-21）**：选用现有 `PrimaryAttackVerification.Verify()`，它在非 Play Mode 预览场景中检查攻击请求去重、执行复核、拒绝与取消、冷却和房间实例 ID。`CiCompileCheck.RunPrimaryAttackVerification()` 在原编译成功标记后调用该方法；验证抛异常时记录异常并调用 `EditorApplication.Exit(1)`。工作流仍只由 `main` push 自动触发，不新增测试分支或手动触发入口，不构建客户端。使用者推送提交 `fe7e11d` 后提供截图，显示 `Unity compile #2` 在 `main` 上成功、用时约 11 分 9 秒，并确认日志同时出现 `[PrimaryAttackRequests] PASS` 与 `[CI] Primary attack verification completed.`。本步正常路径通过；按使用者选择未演练故意失败路径，因此异常时返回非零尚无实际 CI 证据。助手未运行 GitHub 或 Unity。

## 阶段二：自动构建测试客户端

### 3. 生成 Windows 客户端测试产物

**本步工作**：在前两步成功后，自动构建 Windows 客户端并上传为 CI 产物。先使用手动触发或明确的测试分支触发，避免每次备份式 push 都执行耗时构建。产物标明 Git 提交和构建时间。

**验收**：从 CI 下载产物，在 Windows 上启动并进入原有场景；与本机手动构建的关键流程一致。失败构建不得被当作可测试产物。

**验收记录（2026-11-21）**：已核对 `ProjectSettings/EditorBuildSettings.asset`，`MainMenu.unity` 和 `MainScene.unity` 均启用。新增 `.github/workflows/windows-test-build.yml`，仅能手动触发，且构建 job 只接受仓库 `yunxi1732/myrpg` 的 `main`；使用现有三个 Unity Actions Secrets 和 GameCI 构建 `StandaloneWindows64`。只有 GameCI 成功并找到 Windows `.exe` 与匹配的 `_Data` 目录后，才写入 `build-info.txt`（Git 提交及 UTC 构建时间）并上传产物；产物名也包含提交和 UTC 时间，保留 14 天。不部署、不改资源或玩法。使用者手动触发构建，提供的截图显示 `Windows test build #1` 在提交 `20d41a9` 上成功并有 1 个产物；随后确认下载、解压及客户端操作没有问题。第 3 步据此验收，客户端细节依据使用者反馈，助手未独立运行 GitHub 或 Unity。

## 阶段三：完成现有项目的资源热更新

### 4. 接入 Addressables，并一次性完成现有资源迁移

**本步工作**：先确认测试资源服务器和发布权限。在同一个实施阶段内，接入 Addressables，梳理项目里实际使用的资源及加载入口，把适合更新的现有资源及其依赖全部迁入远程资源组，同时修改相应的直接引用、Resources 加载、场景加载、对象池和联机加载入口。启动必需内容留在客户端，编辑器专用内容不作为运行时资源发布。实现版本检查、下载进度、失败重试、离线使用已有内容和回退；保存内容构建状态，并让 CI 生成可供人工审核的资源包与版本记录。日常 push 不自动发布资源。开发过程中可以多次提交和测试，但不以单个示例资源或单个资源组作为本阶段验收。

**验收**：逐项核对现有项目运行时资源，没有遗漏或原因不明的随包资源。用同一个已发布的旧客户端获取更新后的资源，检查菜单、场景、画面、动画、音频、战斗、背包、存档和 A/B 双客户端；再检查断网、下载中断、重试及回退。确认现有可更新资源都通过后，才算资源阶段完成。新增资源也必须经过同一套构建与检查。

**实施与验证记录（2026-11-21）**：使用者安装 Addressables `2.9.1`；编辑器配置脚本按 MainScene 实际依赖建立 279 个远程运行资源条目。MainMenu 保留为客户端首场景，两个 TextMesh Pro `Resources` 字体依赖随客户端保留；编辑器资源及未被当前运行场景使用的素材不发布。资源清单和 Unity 构建布局见 `Docs/HotUpdateResourceInventory.csv`、`Docs/HotUpdateImplementation.md`。本机以 `ServerData/` 为 `127.0.0.1:8000` 的 HTTP 根目录，仅用于测试。

运行时已接入版本清单与 Catalog 校验、下载进度、失败重试、首次下载后的离线缓存、上一资源版本回退；MainScene 改由 Addressables 加载，联机握手拒绝不同资源版本。保存 `addressables_content_state.bin`、构建布局、SHA-256 校验记录及待审核 `latest.json`。资源文件放在 `ServerData/releases/<releaseId>/StandaloneWindows64/`；人工核对后才将该版本的 `latest.json` 放到 `ServerData/` 根目录。日常 push 不发布资源。

本机首次资源版本 `20261121T130816Z` 和图标更新版本 `20261121T133911Z` 已构建。固定的 `Build/HotUpdateTest/rpg.exe` 未重建即可下载新版，并显示 Dodge 技能的新图标；使用者确认在线进入 MainScene、断网后使用已有内容、回退旧图标、同版本联机及不同版本拒绝加入。空缓存且 HTTP 关闭时显示无可用资源与 Retry；恢复 HTTP 后重试成功。上述是关键路径验证，不能代替验收条目中战斗、背包、存档、动画和音频的逐项回归。

GitHub Actions 手动 `Resource review build` 首次运行 `44912579900` 在清单打包处失败；修复 GameCI 向 Unity 传入 releaseId 和内容状态路径后，提交 `4a68273` 的运行 [#2](https://github.com/yunxi1732/myrpg/actions/runs/44914497120) 中 Addressables 构建、清单校验和审核产物上传均成功，产物名为 `rpg-resource-ci-44914497120-4a68273ce25df1aa9eca83324d5dcf8674fe9849`。该 CI 产物尚未下载、逐项审核或用于固定旧客户端复测；CI 不会自动发布它。

**剩余验收**：下载并审核 CI 产物及版本记录，用固定旧客户端复测这份产物；按上方验收范围补齐画面、动画、音频、战斗、背包、存档和双客户端完整回归。完成后再把第 4 步标为验收通过。

## 阶段四：完成现有项目的代码热更新

### 5. 接入 HybridCLR，并一次性完成适合热更的现有代码迁移

**本步工作**：在同一个实施阶段内，梳理现有运行时代码及依赖，确定基础客户端必须保留的启动器、稳定接口、Unity 组件入口和 AOT 代码，把其余适合热更的现有游戏逻辑迁入热更程序集。接入 HybridCLR，建立 Windows IL2CPP 基础客户端，完成 AOT 元数据、DLL 加载、版本校验、下载失败恢复和回退。让 CI 生成待审核的 DLL、版本记录与校验信息，人工确认后才发布。开发过程中可以多次提交和测试，但不以一条演示规则作为本阶段验收。

**验收**：现有运行时代码逐项核对，所有适合热更的游戏逻辑都已迁移，必须留在基础客户端的代码写明原因。固定一个已发布的 HybridCLR 客户端，不重新打包，更新 DLL 后验证现有游戏逻辑变化；覆盖菜单、战斗、背包、存档及 A/B 联机。再验证断网、损坏 DLL、版本不兼容和回退。Unity 引擎、原生插件、基础客户端接口或平台配置发生变化时，仍需重新发布客户端。

**程序集迁移与加载记录（2026-11-22）**：已建立 `Rpg.HotUpdate.asmdef`；[逐文件清单](HotUpdateAssemblyInventory.csv)核对了 211 个热更脚本、17 个随包 Bootstrap 脚本和 1 个 HybridCLR 生成的 AOT 引用文件，并记录每个随包脚本的原因。远程场景/预制体保留脚本 GUID，已清除 Skeleton 预制体中的失效脚本。发布清单使用 `scriptAssemblyVersion=2`，Windows IL2CPP 基础客户端 Version 为 `1.1.0`。使用者已完成全量 Addressables 构建与 Windows 客户端构建；同一 `rpg.exe` 和 `GameAssembly.dll` 的哈希不变，更新 DLL 后 `Player.log` 出现新版热更入口标记。CI 已改为在内容构建前生成 HybridCLR DLL 和 AOT 元数据，实际 CI 运行尚未验证；Gitee 安装源的运行器可达性待确认。

**第 5 步剩余验收**：用固定客户端逐项回归菜单、战斗、背包、存档及 A/B 联机；验证断网、损坏 DLL、客户端/协议版本不兼容与回退；下载并审核 CI 产物，核对 DLL、AOT 元数据、资源目录和校验记录。未完成这些验收前，本阶段保持实施中。

## 当前开始位置

第 0～3 步已验收。第 4 步已实现并通过本机关键路径和 CI 构建，仍需完成上述剩余验收；第 5 步的程序集迁移、Windows IL2CPP 客户端、DLL 下载加载和固定客户端替换 DLL 的基础链路已完成，CI 及故障/完整游戏回归仍待验收。
