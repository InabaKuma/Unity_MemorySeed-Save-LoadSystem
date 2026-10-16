# MemorySeed

一个轻量、安全、高鲁棒性的 Unity 通用存档系统。

## ✨ 核心特性

- **安全防篡改**：支持 AES 加密，并每次动态生成随机 IV 写入密文头部，防止玩家轻易修改存档。
- **轻量压缩**：支持 GZip 压缩，极大缩小存档体积。
- **防 IL2CPP 裁剪崩溃**：使用 `FormatterServices.GetUninitializedObject` 绕过构造函数创建实例，避免打包后反序列化闪退。
- **容错与版本兼容**：自带键值类型安全转换（如 int 转 float 不报错），字段增加/减少或类型变更时降级返回默认值。
- **文件缓存机制**：基于文件时间戳校验，避免频繁读写磁盘。

## 🚀 快速开始

1. 将 `MemorySeedCore.cs` 脚本挂载到场景中的任意 GameObject 上，或者在代码中直接调用静态 API。
2. 在 Inspector 面板中可以配置：自定义存档路径、开启 AES 加密及密钥、开启 GZip 压缩。
3. 代码调用示例：

```csharp
// 保存数据
MemorySeedCore.Save("PlayerLevel", 10);

// 读取数据（带默认值容错）
int level = MemorySeedCore.Load("PlayerLevel", defaultValue: 1);

// 删除特定键
MemorySeedCore.DeleteKey("PlayerLevel");
