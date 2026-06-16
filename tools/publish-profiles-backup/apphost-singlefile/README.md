# AppHost single-file publish backup

本目录保留开发期快速验证改造前 AppHost 自包含单文件发布配置副本。

当前用户级安装脚本默认把 AppHost 发布为普通 .NET framework-dependent 程序，以缩短日常验证时间。最终分发若需要恢复自包含单文件，可以参考本目录中的 `win-x64-self-contained-single-file.args.txt`，或在安装 / 分发脚本中重新接回同等发布参数。

