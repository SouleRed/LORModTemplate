using HarmonyLib;
using LOR_DiceSystem;
using LOR_XML;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using Workshop;


namespace TESTMod
{
    public class TESTMod_ModInitializer : ModInitializer
    {

        #region OnInitializeMod（初始化）

        public static string packageId = "TESTMod";
        public static string Language;
        public static string path = Path.GetDirectoryName(Uri.UnescapeDataString(new UriBuilder(Assembly.GetExecutingAssembly().CodeBase).Path));
        public static string logFile = Path.Combine(path, "Init.log");
        public static string errFile = Path.Combine(path, "Error.txt");

        public override void OnInitializeMod()
        {
            // 清空日志
            try { File.WriteAllText(logFile, ""); File.WriteAllText(errFile, ""); } catch { }

            // 写日志（Init.log 追加；Debug 同步）
            void Log(string tag, string msg)
            {
                string line = $"[{tag}] {msg}";
                Debug.Log(line);
                try { File.AppendAllText(logFile, line + Environment.NewLine); } catch { }
            }

            // 执行步骤并捕获异常（异常写 Error.txt）
            void Run(string name, Action act)
            {
                try { act(); Log("Init", $"{name}:OK"); }
                catch (Exception e)
                {
                    Log("Init", $"{name}:FAIL {e.GetType().Name} | {e.Message}");
                    try { File.AppendAllText(errFile, $"[Init][{name}] {e}{Environment.NewLine}"); } catch { }
                }
            }

            Log("Init", "START");

            Harmony harmony = new Harmony("TESTMod");

            Run("InitializeArtWorks", () => InitializeArtWorks(new DirectoryInfo(Path.Combine(path, "Artwork"))));
            Run("AddAssets", () => AddAssets(new DirectoryInfo(Path.Combine(path, "AB"))));
            Run("AddAudioClips", () => AddAudioClips(new DirectoryInfo(Path.Combine(path, "AudioClips"))));
            Run("AddDiceEffect", AddDiceEffect);
            Run("LoadCustomSkins", () => LoadCustomSkins(packageId, path));
            Run("Harmony", () => harmony.PatchAll());
            Run("Language", () => Language = GlobalGameManager.Instance.CurrentOption.language);
            Run("AddLocalize", AddLocalize);

            Log("Init", "END");
        }

        #endregion

        #region AddLocalize（本地化处理）
        public static void AddLocalize()
        {
            // --- 仅错误输出：统一错误记录 ---
            void LogError(string step, Exception ex, string file = null)
            {
                string where = string.IsNullOrEmpty(file) ? step : $"{step} | {Path.GetFileName(file)}";
                string msg = $"[TEST][本地化][{step}]<失败：{ex.GetType().Name}: {ex.Message}{(string.IsNullOrEmpty(file) ? string.Empty : $" | 文件={Path.GetFileName(file)}")}>";
                UnityEngine.Debug.LogError(msg);

                try
                {
                    File.AppendAllText(errFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
                }
                catch { }
            }

            void Run(string step, Action act)
            {
                try { act(); }
                catch (Exception ex) { LogError(step, ex); }
            }

            void ForEachFile(string step, string dir, Action<string> perFile)
            {
                if (!Directory.Exists(dir)) return;
                List<string> files = new List<string>(Directory.EnumerateFiles(dir));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    try { perFile(file); }
                    catch (Exception ex) { LogError(step, ex, file); }
                }
            }

            // --- 基础检查（出错才打印） ---
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(Language))
            {
                LogError("前置检查", new Exception($"path 或 Language 为空。path='{path ?? "null"}', Language='{Language ?? "null"}'"));
                return;
            }

            string locRoot = Path.Combine(path, "Localize");
            string langRoot = Path.Combine(locRoot, Language);

            // --- 0. FormationInfo（不区分语言，位于 Localize 根目录） ---
            Run("0.FormationInfo", () =>
            {
                FormationXmlList inst = Singleton<FormationXmlList>.Instance;
                List<FormationXmlInfo> formationList = inst._list;
                if (formationList == null) throw new Exception("FormationXmlList._list 为空");

                string formationPath = Path.Combine(locRoot, "FormationInfo.txt");
                if (!File.Exists(formationPath)) return;

                XmlSerializer ser = new XmlSerializer(typeof(FormationXmlRoot));
                using (StreamReader sr = new StreamReader(formationPath))
                {
                    FormationXmlRoot root = (FormationXmlRoot)ser.Deserialize(sr);
                    if (root?.list == null) return;
                    foreach (FormationXmlInfo item in root.list)
                    {
                        if (item == null) continue;
                        int index = formationList.FindIndex(x => x != null && x.id == item.id);
                        if (index >= 0) formationList[index] = item;
                        else formationList.Add(item);
                    }
                }
            });

            // --- 1. BattleEffectTexts ---
            Run("1.EffectTexts", () =>
            {
                BattleEffectTextsXmlList inst = Singleton<BattleEffectTextsXmlList>.Instance;
                Dictionary<string, BattleEffectText> effectDict = inst._dictionary;
                if (effectDict == null) throw new Exception("BattleEffectTextsXmlList._dictionary 为空");

                string dir = Path.Combine(langRoot, "EffectTexts");
                XmlSerializer ser = new XmlSerializer(typeof(BattleEffectTextRoot));

                ForEachFile("1.EffectTexts", dir, file =>
                {
                    using (StreamReader sr = new StreamReader(file))
                    {
                        BattleEffectTextRoot root = (BattleEffectTextRoot)ser.Deserialize(sr);
                        if (root?.effectTextList == null) return;

                        foreach (BattleEffectText item in root.effectTextList)
                        {
                            if (item == null || string.IsNullOrEmpty(item.ID)) continue;
                            effectDict[item.ID] = item; // 覆盖写，避免 Add 重复崩溃
                        }
                    }
                });
            });

            // --- 2. BattleCardAbilities ---
            Run("2.BattleCardAbilities", () =>
            {
                BattleCardAbilityDescXmlList inst = Singleton<BattleCardAbilityDescXmlList>.Instance;
                Dictionary<string, BattleCardAbilityDesc> missing = new Dictionary<string, BattleCardAbilityDesc>();
                string dir = Path.Combine(langRoot, "BattleCardAbilities");
                XmlSerializer ser = new XmlSerializer(typeof(LOR_XML.BattleCardAbilityDescRoot));

                ForEachFile("2.BattleCardAbilities", dir, file =>
                {
                    using (StreamReader sr = new StreamReader(file))
                    {
                        BattleCardAbilityDescRoot root = (LOR_XML.BattleCardAbilityDescRoot)ser.Deserialize(sr);
                        if (root?.cardDescList == null) return;

                        foreach (BattleCardAbilityDesc cardDesc in root.cardDescList)
                        {
                            if (cardDesc == null || string.IsNullOrWhiteSpace(cardDesc.id)) continue;
                            BattleCardAbilityDesc data = inst.GetData(cardDesc.id);
                            if (data != null) data.desc = cardDesc.desc ?? new List<string>();
                            else
                            {
                                cardDesc.desc = cardDesc.desc ?? new List<string>();
                                missing[cardDesc.id] = cardDesc;
                            }
                        }
                    }
                });
                if (missing.Count > 0) inst.AddByMode(packageId, missing);
            });

            // --- 3. BattleDialogues ---
            Run("3.BattleDialogues", () =>
            {
                BattleDialogXmlList inst = Singleton<BattleDialogXmlList>.Instance;
                Dictionary<string, LOR_XML.BattleDialogRoot> dialogDict = inst._dictionary;
                if (dialogDict == null) throw new Exception("BattleDialogXmlList._dictionary 为空");

                string dir = Path.Combine(langRoot, "BattleDialogues");
                XmlSerializer ser = new XmlSerializer(typeof(LOR_XML.BattleDialogRoot));

                ForEachFile("3.BattleDialogues", dir, file =>
                {
                    using (StreamReader sr = new StreamReader(file))
                    {
                        BattleDialogRoot root = (LOR_XML.BattleDialogRoot)ser.Deserialize(sr);
                        if (root == null || string.IsNullOrEmpty(root.groupName)) return;
                        dialogDict[root.groupName] = root; // 覆盖写
                    }
                });
            });

            // --- 4. Etc ---
            Run("4.Etc", () =>
            {
                string dir = Path.Combine(langRoot, "etc");
                ForEachFile("4.Etc", dir, file =>
                {
                    XmlDocument xml = new XmlDocument();
                    xml.Load(file);

                    XmlNodeList nodes = xml.SelectNodes("localize/text");
                    if (nodes == null) return;

                    foreach (XmlNode n in nodes)
                    {
                        string idAttr = n?.Attributes?["id"]?.InnerText;
                        if (string.IsNullOrEmpty(idAttr)) continue;
                        TextDataModel.textDic[idAttr] = n.InnerText; // 覆盖写，避免重复 Add 崩溃
                    }
                });
            });

            // 只处理当前语言目录中实际存在的文件；中文与其他语言使用同一套读取规则。
                // --- 5. BattlesCards ---
                Run("5.BattlesCards", () =>
                {
                    Dictionary<LorId, BattleCardDesc> cardDescDict = Singleton<BattleCardDescXmlList>.Instance._dictionary;
                    if (cardDescDict == null) throw new Exception("BattleCardDescXmlList._dictionary 为空");

                    string dir = Path.Combine(langRoot, "BattlesCards");
                    XmlSerializer ser = new XmlSerializer(typeof(BattleCardDescRoot));

                    ForEachFile("5.BattlesCards", dir, file =>
                    {
                        BattleCardDescRoot root;
                        using (StreamReader sr = new StreamReader(file)) root = (BattleCardDescRoot)ser.Deserialize(sr);

                        if (root?.cardDescList == null) return;

                        // 建表：cardID -> cardName
                        Dictionary<int, string> nameById = new Dictionary<int, string>();
                        foreach (BattleCardDesc d in root.cardDescList)
                        {
                            if (d == null) continue;
                            cardDescDict[new LorId(packageId, d.cardID)] = d;
                            nameById[d.cardID] = d.cardName;
                        }

                        // Workshop 卡牌
                        Dictionary<string, List<DiceCardXmlInfo>> allWorkshop = ItemXmlDataList.instance.GetAllWorkshopData();
                        if (allWorkshop != null && allWorkshop.TryGetValue(packageId, out List<DiceCardXmlInfo> wkCards))
                        {
                            foreach (DiceCardXmlInfo card in wkCards)
                            {
                                if (card == null) continue;
                                if (nameById.TryGetValue(card.id.id, out string nm)) card.workshopName = nm;
                            }
                        }

                        // 本地卡表
                        foreach (DiceCardXmlInfo card in ItemXmlDataList.instance.GetCardList().FindAll(x => x.id.packageId == packageId))
                        {
                            if (nameById.TryGetValue(card.id.id, out string nm))
                            {
                                card.workshopName = nm;
                                DiceCardXmlInfo item = ItemXmlDataList.instance.GetCardItem(card.id);
                                if (item != null) item.workshopName = nm;
                            }
                        }
                    });
                });

                // --- 6. CharactersName ---
                Run("6.CharactersName", () =>
                {
                    string dir = Path.Combine(langRoot, "CharactersName");
                    XmlSerializer ser = new XmlSerializer(typeof(CharactersNameRoot));

                    // Workshop 敌人列表（避免重复取）
                    Dictionary<string, List<EnemyUnitClassInfo>> allWorkshop = Singleton<EnemyUnitClassInfoList>.Instance.GetAllWorkshopData();
                    if (allWorkshop == null || !allWorkshop.TryGetValue(packageId, out List<EnemyUnitClassInfo> enemies)) return;

                    ForEachFile("6.CharactersName", dir, file =>
                    {
                        CharactersNameRoot root;
                        using (StreamReader sr = new StreamReader(file)) root = (CharactersNameRoot)ser.Deserialize(sr);

                        if (root?.nameList == null) return;

                        Dictionary<int, string> nameById = new Dictionary<int, string>();
                        foreach (CharacterName n in root.nameList)
                        {
                            if (n == null) continue;
                            nameById[n.ID] = n.name;
                        }

                        foreach (EnemyUnitClassInfo enemy in enemies)
                        {
                            if (enemy == null) continue;
                            if (nameById.TryGetValue(enemy.id.id, out string nm))
                            {
                                enemy.name = nm;
                                EnemyUnitClassInfo data = Singleton<EnemyUnitClassInfoList>.Instance.GetData(enemy.id);
                                if (data != null) data.name = nm;
                            }
                        }
                    });
                });

                // --- 7. Books ---
                Run("7.Books", () =>
                {
                    Dictionary<string, List<LOR_XML.BookDesc>> bookDescDict = Singleton<BookDescXmlList>.Instance._dictionaryWorkshop;
                    if (bookDescDict == null) throw new Exception("BookDescXmlList._dictionaryWorkshop 为空");

                    string dir = Path.Combine(langRoot, "Books");
                    XmlSerializer ser = new XmlSerializer(typeof(BookDescRoot));

                    ForEachFile("7.Books", dir, file =>
                    {
                        BookDescRoot root;
                        using (StreamReader sr = new StreamReader(file)) root = (BookDescRoot)ser.Deserialize(sr);

                        if (root?.bookDescList == null) return;

                        Dictionary<int, string> nameById = new Dictionary<int, string>();
                        foreach (BookDesc b in root.bookDescList)
                        {
                            if (b == null) continue;
                            nameById[b.bookID] = b.bookName;

                            if (!bookDescDict.TryGetValue(packageId, out List<BookDesc> stored) || stored == null)
                            {
                                stored = new List<BookDesc>();
                                bookDescDict[packageId] = stored;
                            }
                            int index = stored.FindIndex(x => x != null && x.bookID == b.bookID);
                            if (index >= 0) stored[index] = b;
                            else stored.Add(b);
                        }

                        // Workshop books
                        Dictionary<string, List<BookXmlInfo>> wkBooks = Singleton<BookXmlList>.Instance.GetAllWorkshopData();
                        if (wkBooks != null && wkBooks.TryGetValue(packageId, out List<BookXmlInfo> booksWk))
                        {
                            foreach (BookXmlInfo book in booksWk)
                            {
                                if (book == null) continue;
                                if (nameById.TryGetValue(book.id.id, out string nm)) book.InnerName = nm;
                            }
                        }

                        // 本地 books
                        foreach (BookXmlInfo book in Singleton<BookXmlList>.Instance.GetList().FindAll(x => x.id.packageId == packageId))
                        {
                            if (nameById.TryGetValue(book.id.id, out string nm))
                            {
                                book.InnerName = nm;
                                BookXmlInfo data = Singleton<BookXmlList>.Instance.GetData(book.id);
                                if (data != null) data.InnerName = nm;
                            }
                        }
                    });
                });

                // --- 8. DropBooks ---
                Run("8.DropBooks", () =>
                {
                    string dir = Path.Combine(langRoot, "DropBooks");
                    XmlSerializer ser = new XmlSerializer(typeof(CharactersNameRoot));

                    Dictionary<string, List<DropBookXmlInfo>> wk = Singleton<DropBookXmlList>.Instance.GetAllWorkshopData();
                    if (wk == null || !wk.TryGetValue(packageId, out List<DropBookXmlInfo> wkDrops)) return;

                    ForEachFile("8.DropBooks", dir, file =>
                    {
                        CharactersNameRoot root;
                        using (StreamReader sr = new StreamReader(file)) root = (CharactersNameRoot)ser.Deserialize(sr);

                        if (root?.nameList == null) return;

                        Dictionary<int, string> nameById = new Dictionary<int, string>();
                        foreach (CharacterName n in root.nameList)
                        {
                            if (n == null) continue;
                            nameById[n.ID] = n.name;
                        }

                        foreach (DropBookXmlInfo drop in wkDrops)
                        {
                            if (drop == null) continue;
                            if (nameById.TryGetValue(drop.id.id, out string nm)) drop.workshopName = nm;
                        }

                        foreach (DropBookXmlInfo drop in Singleton<DropBookXmlList>.Instance.GetList().FindAll(x => x.id.packageId == packageId))
                        {
                            if (nameById.TryGetValue(drop.id.id, out string nm))
                            {
                                drop.workshopName = nm;
                                DropBookXmlInfo data = Singleton<DropBookXmlList>.Instance.GetData(drop.id);
                                if (data != null) data.workshopName = nm;
                            }
                        }
                    });
                });

                // --- 9. StageName ---
                Run("9.StageName", () =>
                {
                    string dir = Path.Combine(langRoot, "StageName");
                    XmlSerializer ser = new XmlSerializer(typeof(CharactersNameRoot));

                    Dictionary<string, List<StageClassInfo>> wkStages = Singleton<StageClassInfoList>.Instance.GetAllWorkshopData();
                    if (wkStages == null || !wkStages.TryGetValue(packageId, out List<StageClassInfo> stages)) return;

                    ForEachFile("9.StageName", dir, file =>
                    {
                        CharactersNameRoot root;
                        using (StreamReader sr = new StreamReader(file)) root = (CharactersNameRoot)ser.Deserialize(sr);

                        if (root?.nameList == null) return;

                        Dictionary<int, string> nameById = new Dictionary<int, string>();
                        foreach (CharacterName n in root.nameList)
                        {
                            if (n == null) continue;
                            nameById[n.ID] = n.name;
                        }

                        foreach (StageClassInfo stage in stages)
                        {
                            if (stage == null) continue;
                            if (nameById.TryGetValue(stage.id.id, out string nm)) stage.stageName = nm;
                        }
                    });
                });

                // --- 10. PassiveDesc ---
                Run("10.PassiveDesc", () =>
                {
                    Dictionary<LorId, PassiveDesc> passiveDescDict = Singleton<PassiveDescXmlList>.Instance._dictionary;
                    if (passiveDescDict == null) throw new Exception("PassiveDescXmlList._dictionary 为空");

                    string dir = Path.Combine(langRoot, "PassiveDesc");
                    XmlSerializer ser = new XmlSerializer(typeof(LOR_XML.PassiveDescRoot));

                    ForEachFile("10.PassiveDesc", dir, file =>
                    {
                        PassiveDescRoot root;
                        using (StreamReader sr = new StreamReader(file)) root = (PassiveDescRoot)ser.Deserialize(sr);

                        if (root?.descList == null) return;

                        Dictionary<int, (string name, string desc)> map = new Dictionary<int, (string name, string desc)>();
                        foreach (PassiveDesc d in root.descList)
                        {
                            if (d == null) continue;
                            d.workshopID = packageId;
                            passiveDescDict[d.ID] = d;
                            map[d._id] = (d.name, d.desc);
                        }

                        foreach (PassiveXmlInfo passive in Singleton<PassiveXmlList>.Instance.GetDataAll().FindAll(x => x.id.packageId == packageId))
                        {
                            if (passive == null) continue;
                            if (map.TryGetValue(passive.id.id, out (string name, string desc) v))
                            {
                                passive.name = v.name;
                                passive.desc = v.desc;
                            }
                        }
                    });
                });
        }
        #endregion

        #region InitializeArtWorks（美术资源读取）

        public static Dictionary<string, Sprite> ArtWorks = new Dictionary<string, Sprite>();
        public static void InitializeArtWorks(DirectoryInfo dir)
        {
            if (dir == null || !dir.Exists) return;
            if (dir.GetDirectories().Length != 0)
            {
                DirectoryInfo[] directories = dir.GetDirectories();
                for (int i = 0; i < directories.Length; i++)
                {
                    InitializeArtWorks(directories[i]);
                }
            }
            System.IO.FileInfo[] files = dir.GetFiles();
            foreach (System.IO.FileInfo fileInfo in files)
            {
                if (fileInfo.Name == ".gitkeep") continue;
                Texture2D texture2D = new Texture2D(2, 2);
                texture2D.LoadImage(File.ReadAllBytes(fileInfo.FullName));
                Sprite value = Sprite.Create(texture2D, new Rect(0f, 0f, texture2D.width, texture2D.height), new Vector2(0f, 0f));
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileInfo.FullName);
                ArtWorks[fileNameWithoutExtension] = value;
            }
        }

        #endregion

        #region AddAssets（特效资源读取）

        public static Dictionary<string, AssetBundle> assetBundles = new Dictionary<string, AssetBundle>();
        public static void AddAssets(DirectoryInfo dir)
        {
            if (dir == null || !dir.Exists) return;
            foreach (var sub in dir.GetDirectories()) AddAssets(sub);
            foreach (var fileInfo in dir.GetFiles())
            {
                if (fileInfo.Name == ".gitkeep") continue;
                var ext = fileInfo.Extension.ToLowerInvariant();
                if (ext == ".manifest" || ext == ".meta") continue;
                var ab = AssetBundle.LoadFromFile(fileInfo.FullName);
                if (ab == null) continue;
                string key = Path.GetFileNameWithoutExtension(fileInfo.Name);
                assetBundles[key] = ab;
            }
        }

        #endregion

        #region AudioClips（音乐资源读取）

        public static Dictionary<string, AudioClip> audioClips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        public static void AddAudioClips(DirectoryInfo dir)
        {
            if (dir == null || !dir.Exists) return;
            foreach (var sub in dir.GetDirectories()) AddAudioClips(sub);
            foreach (var f in dir.GetFiles())
            {
                string ext = f.Extension.ToLowerInvariant();
                if (ext != ".wav" && ext != ".mp3") continue;

                try
                {
                    string key = Path.GetFileNameWithoutExtension(f.Name);
                    AudioClip clip = (ext == ".mp3") ? Mp3ToAudioClip_ShiStyle(f.FullName) : WavToAudioClip_ShiStyle(f.FullName);
                    if (clip != null) audioClips[key] = clip; // 覆盖写，不改变正常行为
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Audio] Load failed: {f.FullName}\n{e}");
                }
            }
        }

        private static AudioClip Mp3ToAudioClip_ShiStyle(string mp3Path)
        {
            string tempWav = mp3Path + ".wav";
            try
            {
                using (var mp3 = new Mp3FileReader(mp3Path)) WaveFileWriter.CreateWaveFile(tempWav, mp3);
                return WavToAudioClip_ShiStyle(tempWav);
            }
            finally
            {
                try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
            }
        }

        private static AudioClip WavToAudioClip_ShiStyle(string wavPath)
        {
            var w = new WAV(File.ReadAllBytes(wavPath));
            if (w.LeftChannel == null || w.SampleCount <= 0 || w.ChannelCount <= 0) return null;

            // Unity多声道 SetData 需要交错数据：L0,R0,L1,R1...
            float[] interleaved;
            if (w.ChannelCount == 1)
            {
                interleaved = w.LeftChannel;
            }
            else
            {
                interleaved = new float[w.SampleCount * 2];
                for (int i = 0; i < w.SampleCount; i++)
                {
                    interleaved[i * 2] = w.LeftChannel[i];
                    interleaved[i * 2 + 1] = w.RightChannel[i];
                }
            }

            var clip = AudioClip.Create(
                Path.GetFileNameWithoutExtension(wavPath),
                w.SampleCount,
                w.ChannelCount,
                w.Frequency,
                stream: false
            );
            clip.SetData(interleaved, 0);
            return clip;
        }

        public class WAV
        {
            public float[] LeftChannel { get; internal set; }
            public float[] RightChannel { get; internal set; }
            public int ChannelCount { get; internal set; }
            public int SampleCount { get; internal set; }
            public int Frequency { get; internal set; }

            private static float BytesToFloat(byte firstByte, byte secondByte) => (short)((secondByte << 8) | firstByte) / 32768f;

            private static int bytesToInt(byte[] bytes, int offset = 0)
            {
                int value = 0;
                for (int i = 0; i < 4; i++) value |= bytes[offset + i] << (i * 8);
                return value;
            }

            public WAV(byte[] wav)
            {
                ChannelCount = wav[22];
                Frequency = bytesToInt(wav, 24);

                int pos = 12;
                while (pos + 4 < wav.Length && (wav[pos] != 'd' || wav[pos + 1] != 'a' || wav[pos + 2] != 't' || wav[pos + 3] != 'a'))
                {
                    pos += 4;
                    int chunkSize = wav[pos] + wav[pos + 1] * 256 + wav[pos + 2] * 65536 + wav[pos + 3] * 16777216;
                    pos += 4 + chunkSize;
                }
                pos += 8;
                SampleCount = (wav.Length - pos) / 2;
                if (ChannelCount == 2) SampleCount /= 2;
                LeftChannel = new float[SampleCount];
                RightChannel = (ChannelCount == 2) ? new float[SampleCount] : null;
                int i = 0;
                while (pos + 1 < wav.Length && i < SampleCount)
                {
                    LeftChannel[i] = BytesToFloat(wav[pos], wav[pos + 1]);
                    pos += 2;
                    if (ChannelCount == 2)
                    {
                        RightChannel[i] = BytesToFloat(wav[pos], wav[pos + 1]);
                        pos += 2;
                    }
                    i++;
                }
            }
        }

        #endregion

        #region AddDiceEffect（骰子特效读取）

        public static Dictionary<string, Type> DiceEffects = new Dictionary<string, Type>();
        public static void AddDiceEffect()
        {
            Type[] types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (Type type in types)
            {
                if (type.Name.StartsWith("DiceAttackEffect_"))
                {
                    DiceEffects.Add(type.Name.Replace("DiceAttackEffect_".ToString(), string.Empty), type);
                }
            }
        }

        #endregion

        #region LoadCustomSkins（皮肤读取）
        public static void LoadCustomSkins(string packageId, string modRootPath)
        {
            string skinRoot = Path.Combine(modRootPath, "..", "Resource", "CharacterSkin");
            if (!Directory.Exists(skinRoot)) return;

            foreach (string dir in Directory.GetDirectories(skinRoot))
            {
                string folderName = new DirectoryInfo(dir).Name;
                WorkshopSkinData gameData = Singleton<CustomizingBookSkinLoader>.Instance.GetWorkshopBookSkinData(packageId, folderName);
                if (gameData == null) continue;

                string xmlPath = Path.Combine(dir, "ModInfo.xml");
                if (!File.Exists(xmlPath)) continue;

                try
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(xmlPath);
                    XmlNode clothNode = xmlDoc.SelectSingleNode("ModInfo")?.SelectSingleNode("ClothInfo");
                    if (clothNode == null) continue;

                    int injected = 0;
                    string clothRoot = Path.Combine(dir, "ClothCustom");
                    float GetAttr(XmlNode n, string attr) => float.Parse(n.Attributes[attr].InnerText, CultureInfo.InvariantCulture);

                    for (int i = 12; i <= 30; i++)
                    {
                        ActionDetail action = (ActionDetail)i;
                        if (action == ActionDetail.Special || action == ActionDetail.Slash2 || action == ActionDetail.Penetrate2 || action == ActionDetail.Hit2) continue;

                        XmlNode node = clothNode.SelectSingleNode(action.ToString()) ?? clothNode.SelectSingleNode("Penetrate");
                        XmlNode pNode = node?.SelectSingleNode("Pivot");
                        XmlNode hNode = node?.SelectSingleNode("Head");
                        if (node == null || pNode == null || hNode == null) continue;

                        string png = Path.Combine(clothRoot, node.Name + ".png");
                        string frontPng = Path.Combine(clothRoot, node.Name + "_front.png");
                        bool headEnabled = true;
                        if (hNode.Attributes["head_enable"] != null) bool.TryParse(hNode.Attributes["head_enable"].InnerText, out headEnabled);
                        gameData.dic[action] = new ClothCustomizeData
                        {
                            spritePath = png,
                            frontSpritePath = frontPng,
                            hasFrontSprite = File.Exists(frontPng),
                            hasFrontSpriteFile = File.Exists(frontPng),
                            hasSpriteFile = File.Exists(png),
                            pivotPos = new Vector2((GetAttr(pNode, "pivot_x") + 512f) / 1024f, (GetAttr(pNode, "pivot_y") + 512f) / 1024f),
                            headPos = new Vector2(GetAttr(hNode, "head_x") / 100f, GetAttr(hNode, "head_y") / 100f),
                            headRotation = GetAttr(hNode, "rotation"),
                            headEnabled = headEnabled,
                            direction = node.SelectSingleNode("Direction")?.InnerText == "Side" ? CharacterMotion.MotionDirection.SideView : CharacterMotion.MotionDirection.FrontView
                        };
                        injected++;
                    }

                    if (injected > 0) Debug.Log($"[TEST][皮肤补丁][LoadCustomSkins]<已注入数据：{folderName}，动作数：{injected}>");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TEST][皮肤补丁][LoadCustomSkins]<{folderName} XML解析错误：{ex.Message}>");
                }
            }
        }

        #endregion

    }
}
