using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MemorySeed
{
    /// <summary>
    /// 通用存档系统核心类，可挂载到场景物体上以便在 Inspector 中配置，
    /// 也可直接通过静态方法使用。
    /// </summary>
    public class MemorySeedCore : MonoBehaviour
    {
        // ---------- 静态配置 ----------
        private static string _defaultSaveFolder;
        private static string defaultSaveFolder
        {
            get
            {
                if (string.IsNullOrEmpty(_defaultSaveFolder))
                    _defaultSaveFolder = Application.persistentDataPath + "/Save";
                return _defaultSaveFolder;
            }
            set => _defaultSaveFolder = value;
        }

        private static bool useEncryption = false;
        private static string encryptionPassword = "Default";
        private static bool useCompression = false;

        // 缓存：文件完整路径 -> 键值字典（附带时间戳，解决外部修改问题）
        private class CacheData
        {
            public Dictionary<string, object> Data;
            public DateTime LastWriteTime;
        }
        private static Dictionary<string, CacheData> fileCache = new Dictionary<string, CacheData>();

        // ---------- Inspector 可配置项 ----------
        [Header("存档设置")]
        [SerializeField] private string customSaveFolder = "";
        [SerializeField] private bool enableEncryption = false;
        [SerializeField] private string encryptionKey = "Default";
        [SerializeField] private bool enableCompression = false;

        public static MemorySeedCore Instance;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (!string.IsNullOrEmpty(customSaveFolder))
                defaultSaveFolder = customSaveFolder;
            useEncryption = enableEncryption;
            encryptionPassword = encryptionKey;
            useCompression = enableCompression;
        }

        // ---------- 公共静态 API ----------

        public static void Save<T>(string key, T value, string file = "SaveFile.save")
        {
            string fullPath = GetFullPath(file);
            Dictionary<string, object> data = LoadFileData(fullPath);
            data[key] = value;
            WriteFileData(fullPath, data);
        }

        /// <summary>
        /// 从指定存档文件加载一个值（已加入类型安全转换）
        /// </summary>
        public static T Load<T>(string key, string file = "SaveFile.save", T defaultValue = default)
        {
            string fullPath = GetFullPath(file);
            if (!File.Exists(fullPath))
                return defaultValue;

            Dictionary<string, object> data = LoadFileData(fullPath);
            if (data.TryGetValue(key, out object value))
            {
                try
                {
                    // 安全转换：防止版本更新导致 int 变 float 时直接崩溃
                    if (value is T typedValue) return typedValue;
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    Debug.LogWarning($"[MemorySeed] 键 '{key}' 的类型转换失败，返回默认值。");
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        public static bool KeyExists(string key, string file = "SaveFile.save")
        {
            string fullPath = GetFullPath(file);
            if (!File.Exists(fullPath)) return false;
            Dictionary<string, object> data = LoadFileData(fullPath);
            return data.ContainsKey(key);
        }

        public static void DeleteKey(string key, string file = "SaveFile.save")
        {
            string fullPath = GetFullPath(file);
            if (!File.Exists(fullPath)) return;
            Dictionary<string, object> data = LoadFileData(fullPath);
            if (data.Remove(key))
                WriteFileData(fullPath, data);
        }

        public static void DeleteFile(string file = "SaveFile.save")
        {
            string fullPath = GetFullPath(file);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            if (fileCache.ContainsKey(fullPath))
                fileCache.Remove(fullPath);
        }

        public static bool FileExists(string file = "SaveFile.save")
        {
            return File.Exists(GetFullPath(file));
        }

        public static void ClearCache()
        {
            fileCache.Clear();
        }

        public static void SetSaveFolder(string folderPath)
        {
            defaultSaveFolder = folderPath;
            if (!Directory.Exists(defaultSaveFolder))
                Directory.CreateDirectory(defaultSaveFolder);
        }

        public static void SetEncryption(bool enabled, string password = null)
        {
            useEncryption = enabled;
            if (password != null)
                encryptionPassword = password;
        }

        public static void SetCompression(bool enabled)
        {
            useCompression = enabled;
        }

        // ---------- 内部辅助方法 ----------

        private static string GetFullPath(string file)
        {
            if (!Directory.Exists(defaultSaveFolder))
                Directory.CreateDirectory(defaultSaveFolder);
            return Path.Combine(defaultSaveFolder, file);
        }

        private static Dictionary<string, object> LoadFileData(string fullPath)
        {
            // 缓存过期检查
            if (fileCache.TryGetValue(fullPath, out var cached))
            {
                if (File.Exists(fullPath) && File.GetLastWriteTime(fullPath) == cached.LastWriteTime)
                    return cached.Data;
            }

            if (!File.Exists(fullPath))
                return new Dictionary<string, object>();

            byte[] bytes = File.ReadAllBytes(fullPath);
            if (useCompression)
                bytes = Decompress(bytes);
            if (useEncryption)
                bytes = Decrypt(bytes);

            Dictionary<string, object> data = DeserializeDictionary(bytes);

            // 更新缓存
            fileCache[fullPath] = new CacheData
            {
                Data = data,
                LastWriteTime = File.GetLastWriteTime(fullPath)
            };
            return data;
        }

        private static void WriteFileData(string fullPath, Dictionary<string, object> data)
        {
            byte[] bytes = SerializeDictionary(data);
            if (useEncryption)
                bytes = Encrypt(bytes);
            if (useCompression)
                bytes = Compress(bytes);

            File.WriteAllBytes(fullPath, bytes);

            fileCache[fullPath] = new CacheData
            {
                Data = data,
                LastWriteTime = File.GetLastWriteTime(fullPath)
            };
        }

        // ---------- 序列化 / 反序列化 ----------

        private static byte[] SerializeDictionary(Dictionary<string, object> dict)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write(dict.Count);
                foreach (var kvp in dict)
                {
                    writer.Write(kvp.Key);
                    WriteObject(writer, kvp.Value);
                }
                return ms.ToArray();
            }
        }

        private static Dictionary<string, object> DeserializeDictionary(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
            using (var reader = new BinaryReader(ms))
            {
                int count = reader.ReadInt32();
                var dict = new Dictionary<string, object>(count);
                for (int i = 0; i < count; i++)
                {
                    string key = reader.ReadString();
                    object value = ReadObject(reader);
                    dict[key] = value;
                }
                return dict;
            }
        }

        private static void WriteObject(BinaryWriter writer, object obj)
        {
            if (obj == null)
            {
                writer.Write((byte)0);
                return;
            }

            Type type = obj.GetType();
            writer.Write((byte)1);
            writer.Write(type.AssemblyQualifiedName);

            if (type == typeof(int)) writer.Write((int)obj);
            else if (type == typeof(float)) writer.Write((float)obj);
            else if (type == typeof(double)) writer.Write((double)obj);
            else if (type == typeof(bool)) writer.Write((bool)obj);
            else if (type == typeof(string)) writer.Write((string)obj);
            else if (type == typeof(byte)) writer.Write((byte)obj);
            else if (type == typeof(short)) writer.Write((short)obj);
            else if (type == typeof(long)) writer.Write((long)obj);
            else if (type == typeof(char)) writer.Write((char)obj);
            else if (type == typeof(decimal)) writer.Write((decimal)obj);
            else if (type.IsEnum) writer.Write((int)obj);
            else if (type.IsArray)
            {
                Array array = (Array)obj;
                writer.Write(array.Length);
                foreach (var item in array) WriteObject(writer, item);
            }
            else if (typeof(IList).IsAssignableFrom(type))
            {
                IList list = (IList)obj;
                writer.Write(list.Count);
                foreach (var item in list) WriteObject(writer, item);
            }
            else if (typeof(IDictionary).IsAssignableFrom(type))
            {
                IDictionary dict = (IDictionary)obj;
                writer.Write(dict.Count);
                foreach (DictionaryEntry entry in dict)
                {
                    WriteObject(writer, entry.Key);
                    WriteObject(writer, entry.Value);
                }
            }
            else
            {
                var fields = GetSerializableFields(type);
                writer.Write(fields.Count);
                foreach (var field in fields)
                {
                    object fieldValue = field.GetValue(obj);
                    WriteObject(writer, fieldValue);
                }
            }
        }

        private static object ReadObject(BinaryReader reader)
        {
            byte isNull = reader.ReadByte();
            if (isNull == 0) return null;

            string typeName = reader.ReadString();
            Type type = Type.GetType(typeName);
            if (type == null)
            {
                // 如果找不到类型（IL2CPP裁剪导致），直接返回 null 而不是抛出异常中断整个存档
                Debug.LogWarning($"[MemorySeed] 无法找到类型: {typeName}，可能被 IL2CPP 裁剪。");
                return null;
            }

            if (type == typeof(int)) return reader.ReadInt32();
            if (type == typeof(float)) return reader.ReadSingle();
            if (type == typeof(double)) return reader.ReadDouble();
            if (type == typeof(bool)) return reader.ReadBoolean();
            if (type == typeof(string)) return reader.ReadString();
            if (type == typeof(byte)) return reader.ReadByte();
            if (type == typeof(short)) return reader.ReadInt16();
            if (type == typeof(long)) return reader.ReadInt64();
            if (type == typeof(char)) return reader.ReadChar();
            if (type == typeof(decimal)) return reader.ReadDecimal();
            if (type.IsEnum) return Enum.ToObject(type, reader.ReadInt32());
            if (type.IsArray)
            {
                int length = reader.ReadInt32();
                Type elementType = type.GetElementType();
                Array array = Array.CreateInstance(elementType, length);
                for (int i = 0; i < length; i++)
                    array.SetValue(ReadObject(reader), i);
                return array;
            }
            if (typeof(IList).IsAssignableFrom(type))
            {
                int count = reader.ReadInt32();
                IList list = (IList)Activator.CreateInstance(type);
                for (int i = 0; i < count; i++)
                    list.Add(ReadObject(reader));
                return list;
            }
            if (typeof(IDictionary).IsAssignableFrom(type))
            {
                int count = reader.ReadInt32();
                IDictionary dict = (IDictionary)Activator.CreateInstance(type);
                for (int i = 0; i < count; i++)
                {
                    object key = ReadObject(reader);
                    object value = ReadObject(reader);
                    dict.Add(key, value);
                }
                return dict;
            }

            // 自定义类或结构体
            object instance = null;
            try
            {
                // 使用 GetUninitializedObject 绕过构造函数，防止类没有无参构造函数时崩溃
                instance = FormatterServices.GetUninitializedObject(type);
            }
            catch
            {
                instance = Activator.CreateInstance(type);
            }

            var fields = GetSerializableFields(type);
            int fieldCount = reader.ReadInt32();
            if (fieldCount != fields.Count)
                Debug.LogWarning($"[MemorySeed] 字段数量不匹配，期望 {fields.Count}，实际 {fieldCount}。可能是版本更新导致。");

            foreach (var field in fields)
            {
                object fieldValue = ReadObject(reader);
                if (type.IsValueType)
                {
                    object boxed = instance;
                    field.SetValue(boxed, fieldValue);
                    instance = boxed;
                }
                else
                {
                    field.SetValue(instance, fieldValue);
                }
            }
            return instance;
        }

        private static List<FieldInfo> GetSerializableFields(Type type)
        {
            var fields = new List<FieldInfo>();
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null)
                    fields.Add(field);
            }
            fields.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            return fields;
        }

        // ---------- 加密与压缩 ----------

        private static byte[] Encrypt(byte[] data)
        {
            using (Aes aes = Aes.Create())
            {
                //  使用 SHA256.Create() 替代过时的 SHA256Managed
                using (var sha256 = SHA256.Create())
                {
                    aes.Key = sha256.ComputeHash(Encoding.UTF8.GetBytes(encryptionPassword));
                }

                // 每次加密随机生成 IV，并写入密文头部
                aes.GenerateIV();

                using (var encryptor = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                {
                    // 把 16 字节的 IV 写进最前面
                    ms.Write(aes.IV, 0, aes.IV.Length);

                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                        cs.Write(data, 0, data.Length);
                    return ms.ToArray();
                }
            }
        }

        private static byte[] Decrypt(byte[] data)
        {
            using (Aes aes = Aes.Create())
            {
                using (var sha256 = SHA256.Create())
                {
                    aes.Key = sha256.ComputeHash(Encoding.UTF8.GetBytes(encryptionPassword));
                }

                // 修复 3：从密文头部读取随机 IV
                byte[] iv = new byte[16];
                Array.Copy(data, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                using (var ms = new MemoryStream(data, iv.Length, data.Length - iv.Length))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var result = new MemoryStream())
                {
                    cs.CopyTo(result);
                    return result.ToArray();
                }
            }
        }

        private static byte[] Compress(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var gzip = new GZipStream(ms, CompressionMode.Compress, true))
                    gzip.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        private static byte[] Decompress(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var gzip = new GZipStream(ms, CompressionMode.Decompress))
            using (var result = new MemoryStream())
            {
                gzip.CopyTo(result);
                return result.ToArray();
            }
        }
    }
}