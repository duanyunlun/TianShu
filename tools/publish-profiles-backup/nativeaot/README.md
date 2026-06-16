# NativeAOT publish profile backup

本目录保留开发期快速验证改造前 CLI 与 ConfigGUI 的 NativeAOT 发布配置副本。

当前用户级安装脚本默认把 CLI 与 ConfigGUI 发布为普通 .NET framework-dependent 程序，以缩短日常验证时间。最终分发若需要恢复 NativeAOT，可以参考本目录中的备份配置，或将对应项目的 `Properties/PublishProfiles/win-x64-nativeaot.pubxml` 重新接回发布脚本。
