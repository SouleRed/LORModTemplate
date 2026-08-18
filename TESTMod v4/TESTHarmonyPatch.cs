using Battle.DiceAttackEffect;
using HarmonyLib;
using LOR_BattleUnit_UI;
using LOR_DiceSystem;
using Mod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.UI;
using Workshop;


namespace TESTMod
{
    public class TESTHarmonyPatch
    {

        #region 骰子特效写入

        [HarmonyPatch(typeof(DiceEffectManager), "CreateBehaviourEffect")]
        public static class DiceEffectManager_CreateBehaviourEffect_Prefix
        {
            [HarmonyPrefix]
            public static bool Prefix( ref DiceAttackEffect __result, string resource, float scaleFactor, BattleUnitView self, BattleUnitView target, float time = 1f)
            {
                if (TESTMod_ModInitializer.DiceEffects.ContainsKey(resource))
                {
                    DiceAttackEffect diceAttackEffect = new GameObject().AddComponent(TESTMod_ModInitializer.DiceEffects[resource]) as DiceAttackEffect;
                    diceAttackEffect.Initialize(self, target, time);
                    diceAttackEffect.SetScale(scaleFactor);
                    __result = diceAttackEffect;
                    return false;
                }
                return true;
            }
        }

        #endregion

        #region 皮肤扩展补丁

        [HarmonyPatch(typeof(WorkshopSkinDataSetter), nameof(WorkshopSkinDataSetter.SetData), new Type[] { typeof(WorkshopSkinData) })]
        public static class WorkshopSkinDataSetter_SetData
        {
            [HarmonyPrefix]
            public static void Prefix(WorkshopSkinDataSetter __instance, WorkshopSkinData data)
            {
                if (data?.dic == null || data.contentFolderIdx != TESTMod_ModInitializer.packageId) return;
                CharacterAppearance appearance = __instance.Appearance;
                if (appearance._motionList.Count == 0) return;
                bool added = false;
                foreach (ActionDetail action in data.dic.Keys)
                {
                    if (appearance.GetCharacterMotion(action) != null) continue;
                    GameObject newObj = UnityEngine.Object.Instantiate(appearance._motionList[0].gameObject, appearance._motionList[0].transform.parent);
                    newObj.name = "Custom_" + action;
                    CharacterMotion motion = newObj.GetComponent<CharacterMotion>();
                    motion.actionDetail = action;
                    motion.motionSpriteSet.Clear();
                    motion.motionSpriteSet.Add(new SpriteSet(motion.transform.GetChild(1).GetComponent<SpriteRenderer>(), CharacterAppearanceType.Body));
                    motion.motionSpriteSet.Add(new SpriteSet(motion.transform.GetChild(0).GetChild(0).GetComponent<SpriteRenderer>(), CharacterAppearanceType.Head));
                    motion.motionSpriteSet.Add(new SpriteSet(motion.transform.GetChild(2).GetComponent<SpriteRenderer>(), CharacterAppearanceType.Body));
                    appearance._motionList.Add(motion);
                    added = true;
                }
                if (added)
                {
                    appearance._initialized = false;
                    appearance.Initialize("");
                }
            }
        }

        [HarmonyPatch(typeof(BattleUnitView), nameof(BattleUnitView.ChangeSkin), new Type[] { typeof(string) })]
        public static class BattleUnitView_ChangeSkin
        {
            [HarmonyPostfix]
            private static void Postfix(BattleUnitView __instance, string charName)
            {
                if (string.IsNullOrWhiteSpace(charName)) return;
                if (Singleton<CustomizingBookSkinLoader>.Instance.GetWorkshopBookSkinData(TESTMod_ModInitializer.packageId, charName) == null) return;
                BookXmlInfo book = __instance?.model?.Book?.ClassInfo;
                if (book.motionSoundList == null || book.motionSoundList.Count <= 0) return;
                __instance.charAppearance?.soundInfo?.SetMotionSounds(book.motionSoundList, ModUtil.GetMotionSoundPath(Singleton<ModContentManager>.Instance.GetModPath(book.id.packageId)));
            }
        }

        [HarmonyPatch(typeof(SpriteUtil), nameof(SpriteUtil.LoadLargePivotSprite))]
        public static class SpriteUtil_LoadLargePivotSprite
        {
            [HarmonyPrefix]
            public static bool Prefix(string filePath, Vector2 pivot, ref Sprite __result)
            {
                if (string.IsNullOrEmpty(filePath)) return true;
                string fullPath = Path.GetFullPath(filePath).Replace('\\', '/');
                if (fullPath.IndexOf("/CharacterSkin/Player1/ClothCustom/", StringComparison.OrdinalIgnoreCase) < 0) return true;

                try
                {
                    byte[] bytes = File.ReadAllBytes(fullPath);
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true);
                    if (!texture.LoadImage(bytes))
                    {
                        UnityEngine.Object.Destroy(texture);
                        return true;
                    }

                    __result = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), pivot, 100f, 0u, SpriteMeshType.FullRect);
                    return false;
                }
                catch (FileNotFoundException ex)
                {
                    Debug.LogError("[TEXT][高清皮肤] LoadLargePivotSprite 拦截失败：" + ex);
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(WorkshopSkinDataSetter), "SetData", new Type[] { typeof(WorkshopSkinData) })]
        public static class WorkshopSkinDataSetter_SetData_AtkPivotsByMotion
        {
            private sealed class MotionEntryConfig
            {
                public string ModId;
                public string SkinId;
                public ActionDetail Motion;
                public Vector2 Pixel;
                public ActionDetail WriteMotion;
                public bool? WriteToSpecial;
            }

            private static readonly MotionEntryConfig[] MotionConfigs =
            {
                new MotionEntryConfig
                {
                    ModId = TESTMod_ModInitializer.packageId,
                    SkinId = "Player1",
                    Motion = ActionDetail.Guard,
                    WriteMotion = ActionDetail.Guard,
                    WriteToSpecial = false,
                    Pixel = new Vector2(230, 490),
                },
                new MotionEntryConfig
                {
                    ModId = TESTMod_ModInitializer.packageId,
                    SkinId = "Player1",
                    Motion = ActionDetail.Slash,
                    WriteMotion = ActionDetail.Slash,
                    WriteToSpecial = false,
                    Pixel = new Vector2(330, 550),
                },
            };

            private static readonly Dictionary<ActionDetail, FieldInfo> PivotFields = new Dictionary<ActionDetail, FieldInfo>
            {
                { ActionDetail.Hit, AccessTools.Field(typeof(CharacterAppearance), "atkEffectPivot_H") },
                { ActionDetail.Slash, AccessTools.Field(typeof(CharacterAppearance), "atkEffectPivot_J") },
                { ActionDetail.Penetrate, AccessTools.Field(typeof(CharacterAppearance), "atkEffectPivot_Z") },
                { ActionDetail.Guard, AccessTools.Field(typeof(CharacterAppearance), "atkEffectPivot_G") },
                { ActionDetail.Evade, AccessTools.Field(typeof(CharacterAppearance), "atkEffectPivot_E") },
                { ActionDetail.Fire, AccessTools.Field(typeof(CharacterAppearance), "atkEffectPivot_F") },
            };

            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(WorkshopSkinDataSetter __instance, WorkshopSkinData data)
            {
                CharacterAppearance appearance = __instance?.Appearance;
                Transform root = appearance?.atkEffectRoot;
                if (appearance == null || root == null || data?.dic == null || data.contentFolderIdx != TESTMod_ModInitializer.packageId) return;

                data.dic.TryGetValue(ActionDetail.Penetrate, out ClothCustomizeData cloth);
                if (string.IsNullOrEmpty(cloth?.spritePath)) data.dic.TryGetValue(ActionDetail.Default, out cloth);
                string skinId = data.dataName;
                if (!string.IsNullOrEmpty(cloth?.spritePath))
                {
                    DirectoryInfo clothDirectory = Directory.GetParent(Path.GetFullPath(cloth.spritePath));
                    if (clothDirectory?.Parent != null) skinId = clothDirectory.Parent.Name;
                }
                if (string.IsNullOrEmpty(skinId)) return;

                foreach (MotionEntryConfig config in MotionConfigs)
                {
                    if (config == null || !config.WriteToSpecial.HasValue || config.WriteMotion == ActionDetail.NONE) continue;
                    if (!string.Equals(config.ModId, data.contentFolderIdx, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(config.SkinId, skinId, StringComparison.OrdinalIgnoreCase)) continue;

                    CharacterMotion motion = appearance.GetCharacterMotion(NormalizeMotion(config.Motion));
                    if (motion == null) continue;

                    Transform rendererTransform = motion.transform.Find("Customize_Renderer");
                    Sprite sprite = rendererTransform?.GetComponent<SpriteRenderer>()?.sprite;
                    if (sprite == null)
                    {
                        rendererTransform = motion.transform.Find("Customize_Renderer_Front");
                        sprite = rendererTransform?.GetComponent<SpriteRenderer>()?.sprite;
                    }
                    if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f) continue;

                    float ppu = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
                    Vector3 spritePoint = new Vector3(
                        (config.Pixel.x - sprite.pivot.x) / ppu,
                        (sprite.rect.height - sprite.pivot.y - config.Pixel.y) / ppu,
                        0f);
                    Vector3 worldPosition = rendererTransform.TransformPoint(spritePoint);

                    if (config.WriteToSpecial.Value)
                    {
                        List<CharacterAppearance.MotionPivot> pivots = appearance._specialMotionPivotList;
                        if (pivots == null)
                        {
                            pivots = new List<CharacterAppearance.MotionPivot>();
                            appearance._specialMotionPivotList = pivots;
                        }

                        CharacterAppearance.MotionPivot entry = pivots.Find(x => x != null && x.motion == config.WriteMotion);
                        Transform pivot = GetOrCreatePivot(root, "specialPivot_" + config.WriteMotion, entry?.pivot);
                        pivot.position = worldPosition;
                        if (entry == null) pivots.Add(new CharacterAppearance.MotionPivot { motion = config.WriteMotion, pivot = pivot });
                        else entry.pivot = pivot;
                        continue;
                    }

                    ActionDetail writeMotion = NormalizeMotion(config.WriteMotion);
                    if (!PivotFields.TryGetValue(writeMotion, out FieldInfo field) || field == null) continue;
                    Transform atkPivot = GetOrCreatePivot(root, field.Name, field.GetValue(appearance) as Transform);
                    field.SetValue(appearance, atkPivot);
                    atkPivot.position = worldPosition;
                }
            }

            private static ActionDetail NormalizeMotion(ActionDetail motion)
            {
                if (motion == ActionDetail.Hit2) return ActionDetail.Hit;
                if (motion == ActionDetail.Slash2) return ActionDetail.Slash;
                if (motion == ActionDetail.Penetrate2) return ActionDetail.Penetrate;
                return motion;
            }

            private static Transform GetOrCreatePivot(Transform root, string nodeName, Transform pivot)
            {
                if (pivot != null && pivot != root) return pivot;
                pivot = root.Find(nodeName);
                if (pivot != null) return pivot;

                pivot = new GameObject(nodeName).transform;
                pivot.SetParent(root, false);
                return pivot;
            }
        }

        #endregion

        #region 原版外部动作音效修复

        [HarmonyPatch(typeof(CharacterSound), "LoadAudioCoroutine", MethodType.Enumerator)]
        public static class CharacterSound_LoadAudioCoroutine
        {
            [HarmonyTranspiler]
            [HarmonyAfter("Cyaminthe.AssortedFixes.Integrated")]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            {
                List<CodeInstruction> codes = instructions.ToList();
                FieldInfo soundMotionField = AccessTools.Field(typeof(CharacterSound.Sound), nameof(CharacterSound.Sound.motion));

                // BaseMod或其他补丁已经补入动作字段时，不再重复注入。
                if (codes.Any(x => x.opcode == OpCodes.Stfld && Equals(x.operand, soundMotionField))) return codes;

                FieldInfo enumeratorField = __originalMethod.DeclaringType
                    ?.GetFields(AccessTools.all)
                    .FirstOrDefault(x => x.FieldType == typeof(List<CharacterSound.ExternalSound>.Enumerator));
                ConstructorInfo soundConstructor = AccessTools.Constructor(typeof(CharacterSound.Sound));
                MethodInfo currentGetter = AccessTools.PropertyGetter(typeof(List<CharacterSound.ExternalSound>.Enumerator), "Current");
                FieldInfo externalMotionField = AccessTools.Field(typeof(CharacterSound.ExternalSound), nameof(CharacterSound.ExternalSound.motion));
                int constructorIndex = codes.FindIndex(x => x.opcode == OpCodes.Newobj && Equals(x.operand, soundConstructor));

                if (enumeratorField == null || soundConstructor == null || currentGetter == null || externalMotionField == null || constructorIndex < 0)
                {
                    Debug.LogError("[TESTMod][动作音效] 无法定位原版外部动作音效IL，未应用修复。");
                    return codes;
                }

                codes.InsertRange(constructorIndex + 1, new[]
                {
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldflda, enumeratorField),
                    new CodeInstruction(OpCodes.Call, currentGetter),
                    new CodeInstruction(OpCodes.Ldfld, externalMotionField),
                    new CodeInstruction(OpCodes.Stfld, soundMotionField)
                });
                return codes;
            }
        }

        #endregion

        #region 原版手牌UI动态扩容修复

        [HarmonyPatch(typeof(BattleUnitCardsInHandUI), nameof(BattleUnitCardsInHandUI.UpdateCardList))]
        public static class BattleUnitCardsInHandUI_UpdateCardList
        {
            private const string CapacityFixMarker = "LoRFix_EnsureHandCardUiCapacity";

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = instructions.ToList();
                if (codes.Any(x => x.operand is MethodInfo method && method.Name == CapacityFixMarker &&
                                   method.ReturnType == typeof(void) && method.GetParameters().Length == 1 &&
                                   method.GetParameters()[0].ParameterType == typeof(BattleUnitCardsInHandUI)))
                    return codes;

                MethodInfo ensureCapacity = AccessTools.Method(typeof(BattleUnitCardsInHandUI_UpdateCardList), CapacityFixMarker);
                if (ensureCapacity == null)
                {
                    Debug.LogError("[TESTMod][手牌UI] 无法定位动态扩容方法，未应用修复。");
                    return codes;
                }
                codes.InsertRange(0, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, ensureCapacity)
                });
                return codes;
            }

            public static void LoRFix_EnsureHandCardUiCapacity(BattleUnitCardsInHandUI ui)
            {
                if (ui == null || !ui.IsActivated()) return;
                BattleUnitModel unit = ui.SelectedModel ?? ui.HOveredModel;
                if (unit == null) return;

                int requiredCount;
                if (ui.CurrentHandState == BattleUnitCardsInHandUI.HandState.BattleCard)
                    requiredCount = unit.allyCardDetail?.GetHand()?.Count ?? 0;
                else
                {
                    requiredCount = unit.personalEgoDetail?.GetHand()?.Count ?? 0;
                    if (unit.Book?.GetBookClassInfoId() != 250022)
                        requiredCount += Singleton<SpecialCardListModel>.Instance?.GetHand()?.Count ?? 0;
                }

                List<BattleDiceCardUI> cardUis = ui.GetCardUIList();
                if (cardUis == null || cardUis.Count == 0) return;
                BattleDiceCardUI template = cardUis[0];
                if (template == null || template.transform.parent == null) return;

                while (cardUis.Count < requiredCount)
                {
                    BattleDiceCardUI cardUi = UnityEngine.Object.Instantiate(template, template.transform.parent);
                    cardUi.name = template.name + "_Extended_" + cardUis.Count;
                    cardUi.gameObject.SetActive(false);
                    cardUis.Add(cardUi);
                }
            }
        }

        // 原版在手牌数>=9时会压缩_xInterval，却仍用固定_xStartInterval计算起点，
        // 导致牌越多整体越偏向左侧。只替换原方法读取起点间隔的IL，不修改UI实例字段。
        [HarmonyPatch(typeof(BattleUnitCardsInHandUI), "ArrangeCardListInLerp")]
        public static class BattleUnitCardsInHandUI_ArrangeCardListInLerp_CenterPatch
        {
            private const string CenterFixMarker = "LoRFix_GetCenteredStartInterval";

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = instructions.ToList();
                if (codes.Any(x => x.operand is MethodInfo method && method.Name == CenterFixMarker &&
                                   method.ReturnType == typeof(float) && method.GetParameters().Length == 2 &&
                                   method.GetParameters()[0].ParameterType == typeof(float) &&
                                   method.GetParameters()[1].ParameterType == typeof(int)))
                    return codes;

                FieldInfo startInterval = AccessTools.Field(typeof(BattleUnitCardsInHandUI), "_xStartInterval");
                FieldInfo xInterval = AccessTools.Field(typeof(BattleUnitCardsInHandUI), "_xInterval");
                MethodInfo getCenteredStartInterval = AccessTools.Method(
                    typeof(BattleUnitCardsInHandUI_ArrangeCardListInLerp_CenterPatch), CenterFixMarker);
                List<int> startIntervalLoads = codes
                    .Select((code, index) => new { code, index })
                    .Where(x => x.code.opcode == OpCodes.Ldfld && Equals(x.code.operand, startInterval))
                    .Select(x => x.index)
                    .ToList();

                if (startInterval == null || xInterval == null || getCenteredStartInterval == null ||
                    startIntervalLoads.Count != 1)
                {
                    Debug.LogError("[TESTMod][手牌UI] 无法唯一定位布局起点间隔IL，未应用居中修复。");
                    return codes;
                }

                int loadIndex = startIntervalLoads[0];
                codes[loadIndex].operand = xInterval;
                codes.InsertRange(loadIndex + 1, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, getCenteredStartInterval)
                });
                return codes;
            }

            // 原方法随后乘以count*0.5；返回这个等效间隔可使步长为2*_xInterval的首尾位置关于x=0对称。
            public static float LoRFix_GetCenteredStartInterval(float xInterval, int count)
            {
                return count > 0 ? 2f * xInterval * (count - 1) / count : 0f;
            }
        }

        #endregion

        #region 原版书页关键词复制修复

        [HarmonyPatch(typeof(DiceCardXmlInfo), nameof(DiceCardXmlInfo.Copy))]
        public static class DiceCardXmlInfo_Copy
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = instructions.ToList();
                FieldInfo keywordsField = AccessTools.Field(typeof(DiceCardXmlInfo), nameof(DiceCardXmlInfo.Keywords));
                if (codes.Any(x => x.opcode == OpCodes.Ldfld && Equals(x.operand, keywordsField))) return codes;

                MethodInfo addRange = AccessTools.Method(typeof(List<string>), nameof(List<string>.AddRange), new[] { typeof(IEnumerable<string>) });
                int returnIndex = codes.FindLastIndex(x => x.opcode == OpCodes.Ret);
                if (keywordsField == null || addRange == null || returnIndex < 0)
                {
                    Debug.LogError("[TESTMod][书页关键词] 无法定位DiceCardXmlInfo.Copy的IL，未应用修复。");
                    return codes;
                }

                CodeInstruction first = new CodeInstruction(OpCodes.Dup);
                first.labels.AddRange(codes[returnIndex].labels);
                codes[returnIndex].labels.Clear();
                codes.InsertRange(returnIndex, new[]
                {
                    first,
                    new CodeInstruction(OpCodes.Ldfld, keywordsField),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, keywordsField),
                    new CodeInstruction(OpCodes.Callvirt, addRange)
                });
                return codes;
            }
        }

        #endregion

        #region 原版速度骰子数字残留修复

        [HarmonyPatch(typeof(SpeedDiceUI), nameof(SpeedDiceUI.ChangeSprite))]
        public static class SpeedDiceUI_ChangeSprite
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = instructions.ToList();
                FieldInfo tensField = AccessTools.Field(typeof(SpeedDiceUI), "img_tensNum");
                FieldInfo unitsField = AccessTools.Field(typeof(SpeedDiceUI), "img_unitsNum");
                MethodInfo gameObjectGetter = AccessTools.PropertyGetter(typeof(Component), nameof(Component.gameObject));
                MethodInfo setActive = AccessTools.Method(typeof(GameObject), nameof(GameObject.SetActive), new[] { typeof(bool) });

                bool HasReset(FieldInfo field)
                {
                    for (int i = 0; i + 3 < codes.Count; i++)
                    {
                        if (codes[i].opcode == OpCodes.Ldfld && Equals(codes[i].operand, field) &&
                            codes[i + 1].Calls(gameObjectGetter) &&
                            codes[i + 2].opcode == OpCodes.Ldc_I4_0 &&
                            codes[i + 3].Calls(setActive))
                            return true;
                    }
                    return false;
                }

                // 原版仅会开启数字图片；检测到两个关闭序列说明其他MOD已经完成修复。
                if (HasReset(tensField) && HasReset(unitsField)) return codes;
                if (tensField == null || unitsField == null || gameObjectGetter == null || setActive == null || codes.Count == 0)
                {
                    Debug.LogError("[TESTMod][速度骰子UI] 无法定位ChangeSprite的IL，未应用修复。");
                    return codes;
                }

                CodeInstruction first = new CodeInstruction(OpCodes.Ldarg_0);
                first.labels.AddRange(codes[0].labels);
                first.blocks.AddRange(codes[0].blocks);
                codes[0].labels.Clear();
                codes[0].blocks.Clear();
                codes.InsertRange(0, new[]
                {
                    first,
                    new CodeInstruction(OpCodes.Ldfld, tensField),
                    new CodeInstruction(OpCodes.Callvirt, gameObjectGetter),
                    new CodeInstruction(OpCodes.Ldc_I4_0),
                    new CodeInstruction(OpCodes.Callvirt, setActive),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, unitsField),
                    new CodeInstruction(OpCodes.Callvirt, gameObjectGetter),
                    new CodeInstruction(OpCodes.Ldc_I4_0),
                    new CodeInstruction(OpCodes.Callvirt, setActive)
                });
                return codes;
            }
        }

        #endregion

        #region OnlyCard 修正

        [HarmonyPatch(typeof(BookModel), "SetXmlInfo")]
        public static class BookModel_SetXmlInfo_Postfix
        {
            [HarmonyPostfix]
            private static void Postfix( BookXmlInfo classInfo, ref List<DiceCardXmlInfo> ____onlyCards)
            {
                if (classInfo?.id.packageId != TESTMod_ModInitializer.packageId) return;
                if (classInfo.EquipEffect?.OnlyCard == null || ____onlyCards == null) return;
                ____onlyCards.Clear();
                foreach (int cardId in classInfo.EquipEffect.OnlyCard)
                {
                    var cardItem = ItemXmlDataList.instance.GetCardItem(new LorId(TESTMod_ModInitializer.packageId, cardId));
                    if (cardItem != null)  ____onlyCards.Add(cardItem);
                }
            }
        }

        #endregion

        #region 关键词修复

        [HarmonyPatch(typeof(BattleCardAbilityDescXmlList), nameof(BattleCardAbilityDescXmlList.GetAbilityKeywords_byScript))]
        public static class BattleCardAbilityDescXmlList_GetAbilityKeywords_byScript_Prefix
        {
            [HarmonyPrefix]
            private static bool Prefix(string scriptName, ref List<string> __result, ref Dictionary<string, List<string>> ____dictionaryKeywordCache)
            {
                if (string.IsNullOrEmpty(scriptName))
                {
                    __result = new List<string>();
                    return false;
                }

                if (____dictionaryKeywordCache.TryGetValue(scriptName, out __result)) return false;

                var v = new List<string>();
                var self = Singleton<AssemblyManager>.Instance.CreateInstance_DiceCardSelfAbility(scriptName);
                if (self != null) v.AddRange(self.Keywords);
                else
                {
                    var dice = Singleton<AssemblyManager>.Instance.CreateInstance_DiceCardAbility(scriptName);
                    if (dice != null) v.AddRange(dice.Keywords);
                    else Debug.LogError("card or dice ability not found : " + scriptName);
                }

                ____dictionaryKeywordCache[scriptName] = v;
                __result = v;
                return false;
            }
        }

        #endregion

        #region 书籍分组UI设置

        [HarmonyPatch(typeof(UIInvenEquipPageListSlot), nameof(UIInvenEquipPageListSlot.SetBooksData))]
        public static class UIInvenEquipPageListSlot_SetBooksData
        {
            [HarmonyPrefix]
            public static bool Prefix(UIInvenEquipPageListSlot __instance, List<BookModel> books, UIStoryKeyData storyKey)
            {
                if (storyKey.workshopId == TESTMod_ModInitializer.packageId)
                {
                    Image image = (Image)__instance.GetType().GetField("img_IconGlow", AccessTools.all).GetValue(__instance);
                    Image image2 = (Image)__instance.GetType().GetField("img_Icon", AccessTools.all).GetValue(__instance);
                    TextMeshProUGUI textMeshProUGUI = (TextMeshProUGUI)__instance.GetType().GetField("txt_StoryName", AccessTools.all).GetValue(__instance);
                    UIEquipPageScrollList listRoot = (UIEquipPageScrollList)__instance.GetType().GetField("listRoot", AccessTools.all).GetValue(__instance);
                    List<UIOriginEquipPageSlot> list = (List<UIOriginEquipPageSlot>)__instance.GetType().GetField("equipPageSlotList", AccessTools.all).GetValue(__instance);
                    if (books.Count >= 0)
                    {
                        image.enabled = true;
                        image.sprite = TESTMod_ModInitializer.ArtWorks["NULL"];
                        image2.enabled = true;
                        image2.sprite = TESTMod_ModInitializer.ArtWorks["NULL"];
                        textMeshProUGUI.text = "TEST";
                    }
                    __instance.SetFrameColor(UIColorManager.Manager.GetUIColor(UIColor.Default));
                    List<BookModel> list2 = new List<BookModel>((List<BookModel>)typeof(UIInvenEquipPageListSlot).GetMethod("ApplyFilterBooksInStory", AccessTools.all).Invoke(__instance, new object[1] { books }));
                    __instance.SetEquipPagesData(list2);
                    BookModel bookModel = list2.Find((BookModel x) => x == UI.UIController.Instance.CurrentUnit.bookItem);
                    if (listRoot.CurrentSelectedBook == null && bookModel != null) listRoot.CurrentSelectedBook = bookModel;
                    if (listRoot.CurrentSelectedBook != null) list.Find((UIOriginEquipPageSlot x) => x.BookDataModel == listRoot.CurrentSelectedBook)?.SetHighlighted(on: true, isClick: true);
                    __instance.SetSlotSize();
                    return false;
                }
                return true;
            }
        }


        [HarmonyPatch(typeof(UISettingInvenEquipPageListSlot), nameof(UISettingInvenEquipPageListSlot.SetBooksData))]
        public static class UISettingInvenEquipPageListSlot_SetBooksData
        {
            [HarmonyPrefix]
            public static bool Prefix(UISettingInvenEquipPageListSlot __instance, List<BookModel> books, UIStoryKeyData storyKey)
            {
                if (storyKey.workshopId == TESTMod_ModInitializer.packageId)
                {
                    Image image = (Image)__instance.GetType().GetField("img_IconGlow", AccessTools.all).GetValue(__instance);
                    Image image2 = (Image)__instance.GetType().GetField("img_Icon", AccessTools.all).GetValue(__instance);
                    TextMeshProUGUI textMeshProUGUI = (TextMeshProUGUI)__instance.GetType().GetField("txt_StoryName", AccessTools.all).GetValue(__instance);
                    UISettingEquipPageScrollList listRoot = (UISettingEquipPageScrollList)__instance.GetType().GetField("listRoot", AccessTools.all).GetValue(__instance);
                    List<UIOriginEquipPageSlot> list = (List<UIOriginEquipPageSlot>)__instance.GetType().GetField("equipPageSlotList", AccessTools.all).GetValue(__instance);
                    if (books.Count >= 0)
                    {
                        image.enabled = true;
                        image.sprite = TESTMod_ModInitializer.ArtWorks["NULL"];
                        image2.enabled = true;
                        image2.sprite = TESTMod_ModInitializer.ArtWorks["NULL"];
                        textMeshProUGUI.text = "TEST";
                    }
                    __instance.SetFrameColor(UIColorManager.Manager.GetUIColor(UIColor.Default));
                    List<BookModel> list2 = new List<BookModel>((List<BookModel>)typeof(UISettingInvenEquipPageListSlot).GetMethod("ApplyFilterBooksInStory", AccessTools.all).Invoke(__instance, new object[1] { books }));
                    __instance.SetEquipPagesData(list2);
                    BookModel bookModel = list2.Find((BookModel x) => x == UI.UIController.Instance.CurrentUnit.bookItem);
                    if (listRoot.CurrentSelectedBook == null && bookModel != null) listRoot.CurrentSelectedBook = bookModel;
                    if (listRoot.CurrentSelectedBook != null) list.Find((UIOriginEquipPageSlot x) => x.BookDataModel == listRoot.CurrentSelectedBook)?.SetHighlighted(on: true, isClick: true);
                    __instance.SetSlotSize();
                    return false;
                }
                return true;
            }
        }

        #endregion

        #region 书籍故事页面分组设置

        [HarmonyPatch(typeof(UIBookStoryChapterSlot), nameof(UIBookStoryChapterSlot.SetEpisodeSlots))]
        public static class UIBookStoryChapterSlot_SetEpisodeSlots
        {
            private const string BookStoryGroupTitle = "TEST";
            private const string BookStoryGroupIconKey = "NULL";

            private static bool IsTESTWorkshopBook(BookXmlInfo book)
            {
                return book?.id?.packageId == TESTMod_ModInitializer.packageId;
            }

            private static UIIconManager.IconSet GetConfiguredBookStoryIconSet()
            {
                UIIconManager.IconSet iconSet = UISpriteDataManager.instance?.GetStoryIcon(BookStoryGroupIconKey);
                if (iconSet != null && iconSet.icon != null) return iconSet;

                if (TESTMod_ModInitializer.ArtWorks != null &&
                    TESTMod_ModInitializer.ArtWorks.TryGetValue(BookStoryGroupIconKey, out Sprite icon) &&
                    icon != null)
                {
                    return new UIIconManager.IconSet { icon = icon, iconGlow = icon };
                }

                return null;
            }

            private static void ApplyConfiguredBookStoryAppearance(UIBookStoryEpisodeSlot slot)
            {
                if (slot == null) return;

                if (slot.episodeText != null)
                {
                    slot.episodeText.text = BookStoryGroupTitle;
                }

                UIIconManager.IconSet iconSet = GetConfiguredBookStoryIconSet();
                if (iconSet == null) return;

                if (slot.episodeIcon != null) slot.episodeIcon.sprite = iconSet.icon;
                if (slot.episodeIconGlow != null) slot.episodeIconGlow.sprite = iconSet.iconGlow;
            }

            [HarmonyPostfix]
            public static void Postfix(UIBookStoryChapterSlot __instance, List<UIBookStoryEpisodeSlot> ___EpisodeSlots)
            {
                if (__instance == null || ___EpisodeSlots == null) return;

                UIBookStoryEpisodeSlot TESTSlot = ___EpisodeSlots.Find(slot =>
                    slot != null &&
                    slot.gameObject.activeSelf &&
                    slot.books != null &&
                    slot.books.Exists(IsTESTWorkshopBook));

                if (TESTSlot == null || TESTSlot.books == null) return;

                List<BookXmlInfo> TESTBooks = TESTSlot.books.FindAll(IsTESTWorkshopBook);
                if (TESTBooks.Count <= 0) return;

                List<BookXmlInfo> remainingBooks = TESTSlot.books.FindAll(book => !IsTESTWorkshopBook(book));

                TESTSlot.Init(TESTBooks, __instance);
                ApplyConfiguredBookStoryAppearance(TESTSlot);

                if (remainingBooks.Count <= 0) return;

                UIBookStoryEpisodeSlot remainingSlot = ___EpisodeSlots.Find(slot =>
                    slot != null &&
                    slot != TESTSlot &&
                    !slot.gameObject.activeSelf);

                if (remainingSlot == null)
                {
                    __instance.InstatiateAdditionalSlot();
                    remainingSlot = ___EpisodeSlots[___EpisodeSlots.Count - 1];
                }

                remainingSlot.Init(__instance.chapter, remainingBooks, __instance);
            }
        }

        [HarmonyPatch(typeof(UIBookStoryPanel), nameof(UIBookStoryPanel.OnSelectEpisodeSlot))]
        public static class UIBookStoryPanel_OnSelectEpisodeSlot
        {
            private const string BookStoryGroupTitle = "TEST";
            private const string BookStoryGroupIconKey = "NULL";

            private static bool IsTESTWorkshopBook(BookXmlInfo book)
            {
                return book?.id?.packageId == TESTMod_ModInitializer.packageId;
            }

            private static UIIconManager.IconSet GetConfiguredBookStoryIconSet()
            {
                UIIconManager.IconSet iconSet = UISpriteDataManager.instance?.GetStoryIcon(BookStoryGroupIconKey);
                if (iconSet != null && iconSet.icon != null) return iconSet;

                if (TESTMod_ModInitializer.ArtWorks != null &&
                    TESTMod_ModInitializer.ArtWorks.TryGetValue(BookStoryGroupIconKey, out Sprite icon) &&
                    icon != null)
                {
                    return new UIIconManager.IconSet { icon = icon, iconGlow = icon };
                }

                return null;
            }

            private static void ApplyConfiguredBookStoryAppearance(TextMeshProUGUI title, Image icon, Image glow)
            {
                if (title != null)
                {
                    title.text = BookStoryGroupTitle;
                }

                UIIconManager.IconSet iconSet = GetConfiguredBookStoryIconSet();
                if (iconSet == null) return;

                if (icon != null) icon.sprite = iconSet.icon;
                if (glow != null) glow.sprite = iconSet.iconGlow;
            }

            [HarmonyPrefix]
            public static bool Prefix(
                UIBookStoryPanel __instance,
                UIBookStoryEpisodeSlot slot,
                TextMeshProUGUI ___selectedEpisodeText,
                Image ___selectedEpisodeIcon,
                Image ___selectedEpisodeIconGlow,
                GameObject ___selectedEpisodeTitleRect,
                GameObject ___nullEpisodeRect,
                Image ___BookListLayout,
                ref List<BookXmlInfo> ___bookListData)
            {
                if (slot?.books == null || !slot.books.Exists(IsTESTWorkshopBook)) return true;

                ___selectedEpisodeTitleRect.SetActive(value: true);
                ___nullEpisodeRect.SetActive(value: false);
                ___BookListLayout.color = UIColorManager.Manager.GetUIColor(UIColor.Default);
                __instance.SelectablePanel_epis.ChildSelectable = slot.selectable;

                ___selectedEpisodeText.gameObject.SetActive(value: true);
                ___selectedEpisodeIcon.gameObject.SetActive(value: true);
                ___selectedEpisodeIconGlow.gameObject.SetActive(value: true);
                ApplyConfiguredBookStoryAppearance(___selectedEpisodeText, ___selectedEpisodeIcon, ___selectedEpisodeIconGlow);

                ___bookListData = slot.books;
                __instance.UpdateBookSlots();
                return false;
            }
        }

        #endregion

        #region 书籍ICON设置

        [HarmonyPatch(typeof(UISpriteDataManager), "GetStoryIcon")]
        public static class UISpriteDataManager_GetStoryIcon_Prefix
        {
            private static readonly HashSet<string> PatchedStoryKeys = new HashSet<string>
            {
                "NULL",
            };

            [HarmonyPrefix]
            private static bool Prefix(ref UIIconManager.IconSet __result, string story)
            {
                if (string.IsNullOrEmpty(story)) return true;
                if (!PatchedStoryKeys.Contains(story)) return true;
                if (TESTMod_ModInitializer.ArtWorks == null) return true;
                if (!TESTMod_ModInitializer.ArtWorks.TryGetValue(story, out Sprite icon) || icon == null) return true;

                __result = new UIIconManager.IconSet{icon = icon, iconGlow = icon };
                return false;
            }
        }

        #endregion

        #region 自动塞书

        public const string PACKAGE_ID = "TESTMod";
        public const int DROP_BOOK_ID = 114514;
        public const int TARGET_STACK = 99;

        [HarmonyPatch(typeof(UIFloorPanel), "CheckOpenFloor")]
        public static class Patch_UIFloorPanel_CheckOpenFloor
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                var id = new LorId(PACKAGE_ID, DROP_BOOK_ID);
                var inv = Singleton<DropBookInventoryModel>.Instance;

                int cur = inv.GetBookCount(id);
                if (cur < TARGET_STACK)
                {
                    inv.AddBook(id, TARGET_STACK - cur);
                }
            }
        }

        #endregion

        #region 剧情多语言补丁

        /// <summary>优先读取当前语言剧情，其次中文剧情，最后读取 StoryText 根目录中的旧版剧情文件。</summary>
        private static string ResolveStoryTextPath(string modPath, string storyFile)
        {
            if (string.IsNullOrEmpty(storyFile)) return null;

            string storyDir = Path.Combine(modPath, "Data", "StoryText");
            List<string> candidates = new List<string>();
            string language = TESTMod_ModInitializer.Language;
            if (!string.IsNullOrEmpty(language))
            {
                candidates.Add(Path.Combine(storyDir, language, storyFile));
                if (language != "cn") candidates.Add(Path.Combine(storyDir, "cn", storyFile));
            }
            candidates.Add(Path.Combine(storyDir, storyFile));

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        [HarmonyPatch(typeof(StorySerializer), nameof(StorySerializer.LoadStageStory))]
        public static class StorySerializer_LoadStageStory_LocalizePrefix
        {
            [HarmonyPriority(Priority.First + 200)]
            [HarmonyPrefix]
            public static bool Prefix(StageStoryInfo stageStoryInfo, ref bool __result)
            {
                if (stageStoryInfo == null || !stageStoryInfo.IsMod || stageStoryInfo.packageId != TESTMod_ModInitializer.packageId) return true;
                try
                {
                    string modPath = Singleton<ModContentManager>.Instance.GetModPath(stageStoryInfo.packageId);
                    if (string.IsNullOrEmpty(modPath)) return true;

                    string storyPath = ResolveStoryTextPath(modPath, stageStoryInfo.story);
                    string effectPath = Path.Combine(modPath, "Data", "StoryEffect", stageStoryInfo.story);
                    if (storyPath != null && StorySerializer.LoadStoryFile(storyPath, effectPath, modPath))
                    {
                        __result = true;
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("[TESTMod][剧情多语言] LoadStageStory 处理异常：" + ex);
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(StorySerializer), nameof(StorySerializer.HasEffectFile))]
        public static class StorySerializer_HasEffectFile_LocalizePrefix
        {
            [HarmonyPriority(Priority.First + 200)]
            [HarmonyPrefix]
            public static bool Prefix(StageStoryInfo stageStoryInfo, ref bool __result)
            {
                if (stageStoryInfo == null || !stageStoryInfo.IsMod || stageStoryInfo.packageId != TESTMod_ModInitializer.packageId) return true;
                try
                {
                    string modPath = Singleton<ModContentManager>.Instance.GetModPath(stageStoryInfo.packageId);
                    __result = !string.IsNullOrEmpty(modPath) && ResolveStoryTextPath(modPath, stageStoryInfo.story) != null;
                    return false;
                }
                catch (Exception ex)
                {
                    Debug.LogError("[TESTMod][剧情多语言] HasEffectFile 处理异常：" + ex);
                }
                return true;
            }
        }

        #endregion

    }
}
