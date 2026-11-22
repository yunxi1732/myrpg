# 第 5 步实际客户端验收

固定客户端：`Build/newBuild/rpg.exe`，Version `1.1.0`，`scriptAssemblyVersion=2`。基础资源版本 `20261122T104506Z`；当前已发布代码版本 `20261122T124206Z`，前一代码版本 `20261122T114153Z`。新 DLL 已在原客户端进入游戏；开始故障与回退测试前先关闭所有客户端进程，记录 `ServerData/latest.json` 与 `Application.persistentDataPath/HotUpdate` 下清单的副本，测试结束后恢复。不要重新构建客户端。

| 场景 | 操作 | 通过条件 |
| --- | --- | --- |
| DLL 替换 | 使用同一 `rpg.exe` 重启，加载代码更新版本 | `Player.log` 有 `[HotUpdate] DLL update probe v2 registered`；本地清单和缓存 DLL SHA-256 为新版；`rpg.exe`、`GameAssembly.dll` 哈希不变。已于 2026-11-22 验证。 |
| 断网缓存 | 已成功下载后，关闭本机 HTTP 服务并重启客户端 | 菜单可进入 NewGame/Continue；本地活动版本不变；不要求再次下载。 |
| 损坏 DLL | 将 `Tools/prepare_hot_update_failure_cases.py` 生成的损坏候选发布到本机测试 HTTP 根目录后重启客户端 | 新候选 DLL 哈希不符，客户端继续使用上一完整版本；清单不晋升到损坏版本；恢复原 `latest.json`。该测试包只供故障注入，不能作为正式发布。 |
| 不兼容清单 | 临时将测试清单的 `clientVersion` 改为不匹配值后重启 | 客户端拒绝候选，继续使用本地兼容缓存；恢复原 `latest.json`。 |
| 回退 | 在 MainMenu 点击 `Rollback resources`，按提示重启 | 当前进程不混用旧资源和新 DLL；重启后活动版本与 DLL 都回到上一完整版本。测试后恢复清单及屏蔽标记。 |
| 菜单 | 依次测试 NewGame、Continue、联机入口、返回 | 入口可用，场景与 UI 无 Missing Script。 |
| 战斗、背包、存档 | 战斗击中/受击，拾取并使用物品，退出后 Continue | 行为正常；存档读回背包和技能状态。 |
| A/B 联机 | 两端同版本建房并加入；再保持 A 旧版、让 B 加载新版重试 | 同版本可进房并同步战斗；异版本在握手时被明确拒绝。 |

CI 审核：手动运行 `Code and resource review build`，下载产物，核对 `release-record.json` 中的客户端版本、协议版本、`scriptAssemblyVersion`、DLL 与 AOT 文件哈希、内容状态和 Git 提交。工作流只上传待审核产物，不写入 `ServerData/latest.json`。当前 CI 安装源使用项目设置中的 Gitee 地址；GitHub 托管运行器的可达性尚未验证。
