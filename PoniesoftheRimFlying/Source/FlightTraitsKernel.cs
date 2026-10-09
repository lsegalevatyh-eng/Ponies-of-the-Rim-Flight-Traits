using System;
using System.Collections.Generic;
using System.Reflection;

using HarmonyLib;
using PoniesOfTheRim.Flying;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FlightTraits
{
    // ============================================================
    // XML-РАСШИРЕНИЕ
    //
    // Используется для:
    // - ThingDef расы;
    // - TraitDef черты;
    // - BackstoryDef предыстории.
    //
    // Имя класса и существующие поля сохранены.
    // ============================================================

    public sealed class FlightTraitExtension : DefModExtension
    {
        // Используется только для черт.
        // Для расы и предыстории degree не проверяется.
        public int degree = 0;

        // Существующие множители.
        public float maxStaminaFactor = 1f;
        public float recoveryFactor = 1f;
        public float flightSpeedFactor = 1f;

        // Дополнительные поправки.
        public float maxStaminaAddTicks = 0f;
        public float recoveryAddPerTick = 0f;
        public float flightSpeedAdd = 0f;

        // Расход выносливости.
        public float staminaDrainFactor = 1f;
        public float staminaDrainAddPerSecond = 0f;

        // Старые поля сохранены для совместимости XML.
        //
        // В этом ядре нет отдельной реализации случайных травм
        // и дополнительного применения generationChance.
        public float generationChance = 0f;
        public float injuryChancePerFlight = 0f;
        public HediffDef wingInjury;
        public List<BodyPartDef> wingBodyParts;
        public float injurySeverity = 1f;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
                yield return error;

            if (!FlightNumbers.Positive(maxStaminaFactor))
            {
                yield return
                    "maxStaminaFactor must be finite and > 0.";
            }

            if (!FlightNumbers.Positive(recoveryFactor))
            {
                yield return
                    "recoveryFactor must be finite and > 0.";
            }

            if (!FlightNumbers.Positive(flightSpeedFactor))
            {
                yield return
                    "flightSpeedFactor must be finite and > 0.";
            }

            if (!FlightNumbers.NonNegative(staminaDrainFactor))
            {
                yield return
                    "staminaDrainFactor must be finite and >= 0.";
            }

            if (!FlightNumbers.Finite(maxStaminaAddTicks))
            {
                yield return
                    "maxStaminaAddTicks must be finite.";
            }

            if (!FlightNumbers.Finite(recoveryAddPerTick))
            {
                yield return
                    "recoveryAddPerTick must be finite.";
            }

            if (!FlightNumbers.Finite(flightSpeedAdd))
            {
                yield return
                    "flightSpeedAdd must be finite.";
            }

            if (!FlightNumbers.Finite(staminaDrainAddPerSecond))
            {
                yield return
                    "staminaDrainAddPerSecond must be finite.";
            }

            if (!FlightNumbers.Probability(generationChance))
            {
                yield return
                    "generationChance must be between 0 and 1.";
            }

            if (!FlightNumbers.Probability(injuryChancePerFlight))
            {
                yield return
                    "injuryChancePerFlight must be between 0 and 1.";
            }

            if (injuryChancePerFlight > 0f)
            {
                if (wingInjury == null)
                    yield return "wingInjury is required.";

                if (wingBodyParts == null || wingBodyParts.Count == 0)
                    yield return "wingBodyParts is required.";

                if (!FlightNumbers.Positive(injurySeverity))
                {
                    yield return
                        "injurySeverity must be finite and > 0.";
                }
            }
        }
    }

    // ============================================================
    // ПАРАМЕТРЫ API
    // ============================================================

    public enum FlightParameter
    {
        // Базовая длительность полёта в тиках.
        DurationTicks,

        // Восстановление: доля выносливости за тик.
        RecoveryPerTick,

        // Коэффициент скорости: 1 = обычная скорость.
        SpeedFactor,

        // Расход: доля выносливости за секунду.
        StaminaDrainPerSecond
    }

    // ============================================================
    // СНИМОК СОСТОЯНИЯ ПЕШКИ ДЛЯ РАСШИРЕНИЙ
    // ============================================================

    public readonly struct FlightContext
    {
        public readonly Pawn Pawn;
        public readonly bool IsEligible;
        public readonly bool IsFlying;
        public readonly bool FlightEnabled;
        public readonly float Stamina;

        internal FlightContext(Pawn pawn)
        {
            Pawn = pawn;
            IsEligible = FlightTraitUtility.IsEligible(pawn);

            CompPegasusFlightToggle toggle = pawn == null
                ? null
                : PonyFlightCache.GetToggle(pawn);

            CompPegasusFlightTimer timer = pawn == null
                ? null
                : PonyFlightCache.GetTimer(pawn);

            FlightEnabled =
                toggle != null && toggle.FlightEnabled;

            IsFlying =
                pawn != null &&
                PonyFlightCache.IsPegasusConstantFlight(pawn);

            Stamina = timer == null
                ? 0f
                : timer.CurrentStaminaPercent;
        }
    }

    // ============================================================
    // ПОПРАВКА ПАРАМЕТРА
    //
    // Итог:
    // (исходное значение + сумма добавок)
    // × произведение множителей.
    // ============================================================

    public struct FlightAdjustment
    {
        public float Add;
        public float Multiply;

        public static FlightAdjustment Identity =>
            new FlightAdjustment
            {
                Add = 0f,
                Multiply = 1f
            };

        public void AddValue(float amount)
        {
            Add += amount;
        }

        public void MultiplyBy(float factor)
        {
            Multiply *= factor;
        }
    }

    // ============================================================
    // КОНТРАКТ ДЛЯ ДРУГИХ C#-МОДУЛЕЙ И МИНИ-МОДОВ
    // ============================================================

    public interface IFlightModifier
    {
        string Id { get; }

        int Priority { get; }

        // Только чтение состояния и изменение adjustment.
        //
        // Нельзя:
        // - менять пешку;
        // - вызывать игровые действия;
        // - использовать случайные числа;
        // - вызывать Evaluate для того же расчёта,
        //   создавая рекурсию.
        void Modify(
            FlightContext context,
            FlightParameter parameter,
            ref FlightAdjustment adjustment);
    }

    // ============================================================
    // ПУБЛИЧНЫЙ API
    // ============================================================

    public static class FlightAPI
    {
        public const int Version = 1;

        private sealed class Entry
        {
            public string Id;
            public int Priority;
            public IFlightModifier Modifier;
        }

        private static readonly object RegistrationLock =
            new object();

        // Расчёты читают готовый снимок регистраций.
        private static volatile Entry[] entries =
            new Entry[0];

        /// <summary>
        /// Подходит ли пешка для системы этого аддона.
        /// Проверяет наличие обоих компонентов полёта.
        /// </summary>
        public static bool IsEligible(Pawn pawn)
        {
            return FlightTraitUtility.IsEligible(pawn);
        }

        /// <summary>
        /// Действующий полёт системы Ponies of the Rim.
        /// Это не просто включённый переключатель.
        /// </summary>
        public static bool IsFlying(Pawn pawn)
        {
            return pawn != null &&
                PonyFlightCache.IsPegasusConstantFlight(pawn);
        }

        /// <summary>
        /// Включён ли переключатель полёта.
        /// Может отличаться от IsFlying.
        /// </summary>
        public static bool IsFlightEnabled(Pawn pawn)
        {
            if (pawn == null)
                return false;

            CompPegasusFlightToggle toggle =
                PonyFlightCache.GetToggle(pawn);

            return toggle != null && toggle.FlightEnabled;
        }

        public static bool HasUsableWings(Pawn pawn)
        {
            return PonyFlightCache.HasUsableWingsFast(pawn);
        }

        public static float GetStamina(Pawn pawn)
        {
            if (pawn == null)
                return 0f;

            CompPegasusFlightTimer timer =
                PonyFlightCache.GetTimer(pawn);

            return timer == null
                ? 0f
                : timer.CurrentStaminaPercent;
        }

        public static FlightContext GetContext(Pawn pawn)
        {
            return new FlightContext(pawn);
        }

        /// <summary>
        /// Регистрировать при запуске мода.
        ///
        /// Для Multiplayer набор модификаторов и порядок
        /// регистрации должны быть согласованы у всех игроков.
        /// Не регистрировать расширения во время игры.
        /// </summary>
        public static void Register(IFlightModifier modifier)
        {
            if (modifier == null)
            {
                throw new ArgumentNullException(
                    nameof(modifier));
            }

            string id = modifier.Id;

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "Flight modifier must have a non-empty Id.");
            }

            lock (RegistrationLock)
            {
                Entry[] current = entries;

                for (int i = 0; i < current.Length; i++)
                {
                    if (string.Equals(
                        current[i].Id,
                        id,
                        StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Duplicate flight modifier: " + id);
                    }
                }

                Entry[] next =
                    new Entry[current.Length + 1];

                Array.Copy(
                    current,
                    next,
                    current.Length);

                next[current.Length] = new Entry
                {
                    Id = id,
                    Priority = modifier.Priority,
                    Modifier = modifier
                };

                Array.Sort(next, (a, b) =>
                {
                    int priority =
                        a.Priority.CompareTo(b.Priority);

                    return priority != 0
                        ? priority
                        : string.CompareOrdinal(a.Id, b.Id);
                });

                entries = next;
            }
        }

        /// <summary>
        /// Учитывает XML расы, черт, предысторий
        /// и зарегистрированные C#-модификаторы.
        ///
        /// Результат нельзя повторно применять к тому же
        /// значению: коэффициенты будут учтены дважды.
        /// </summary>
        public static float Evaluate(
            Pawn pawn,
            FlightParameter parameter,
            float originalValue,
            float minimum = 0f)
        {
            if (pawn == null ||
                !FlightNumbers.Finite(originalValue))
            {
                return originalValue;
            }

            if (!FlightNumbers.Finite(minimum))
                minimum = 0f;

            FlightContext context =
                new FlightContext(pawn);

            FlightAdjustment adjustment =
                FlightAdjustment.Identity;

            // Постоянные XML-поправки.
            FlightTraitUtility.ApplyXml(
                pawn,
                parameter,
                ref adjustment);

            // Дополнительные модули.
            Entry[] snapshot = entries;

            for (int i = 0; i < snapshot.Length; i++)
            {
                FlightAdjustment contribution =
                    FlightAdjustment.Identity;

                snapshot[i].Modifier.Modify(
                    context,
                    parameter,
                    ref contribution);

                adjustment.Add +=
                    FlightNumbers.SafeAdd(contribution.Add);

                adjustment.Multiply *=
                    FlightNumbers.SafeMultiplier(
                        contribution.Multiply);
            }

            double result =
                ((double)originalValue + adjustment.Add)
                * adjustment.Multiply;

            if (double.IsNaN(result) ||
                double.IsInfinity(result))
            {
                return originalValue;
            }

            result = Math.Max(minimum, result);

            return result >= float.MaxValue
                ? float.MaxValue
                : (float)result;
        }

        public static float GetSpeedFactor(Pawn pawn)
        {
            return Evaluate(
                pawn,
                FlightParameter.SpeedFactor,
                1f,
                minimum: 0.01f);
        }
    }

    // ============================================================
    // СОВМЕСТИМОСТЬ СО СТАРЫМИ ОБРАЩЕНИЯМИ
    // ============================================================

    public struct FlightModifiers
    {
        public float MaxStaminaFactor;
        public float RecoveryFactor;
        public float FlightSpeedFactor;

        public static FlightModifiers Identity =>
            new FlightModifiers
            {
                MaxStaminaFactor = 1f,
                RecoveryFactor = 1f,
                FlightSpeedFactor = 1f
            };
    }

    public static class FlightTraitUtility
    {
        public static bool IsEligible(Pawn pawn)
        {
            return pawn != null &&
                pawn.def != null &&
                pawn.story != null &&
                pawn.TryGetComp<CompPegasusFlightTimer>() != null &&
                pawn.TryGetComp<CompPegasusFlightToggle>() != null;
        }

        public static FlightTraitExtension GetExtension(
            Trait trait)
        {
            if (trait == null || trait.def == null)
                return null;

            FlightTraitExtension extension =
                trait.def.GetModExtension<FlightTraitExtension>();

            return extension != null &&
                   extension.degree == trait.Degree
                ? extension
                : null;
        }

        /// <summary>
        /// Совместимый метод для старого кода.
        ///
        /// Возвращает только XML-множители.
        /// Теперь включает и коэффициенты расы.
        /// Добавки и C#-модификаторы здесь не учитываются.
        /// Для полного расчёта использовать FlightAPI.Evaluate.
        /// </summary>
        public static FlightModifiers GetModifiers(Pawn pawn)
        {
            FlightModifiers result =
                FlightModifiers.Identity;

            if (!IsEligible(pawn))
                return result;

            foreach (
                FlightTraitExtension extension
                in GetExtensions(pawn))
            {
                result.MaxStaminaFactor *=
                    FlightNumbers.SafeFactor(
                        extension.maxStaminaFactor);

                result.RecoveryFactor *=
                    FlightNumbers.SafeFactor(
                        extension.recoveryFactor);

                result.FlightSpeedFactor *=
                    FlightNumbers.SafeFactor(
                        extension.flightSpeedFactor);
            }

            result.MaxStaminaFactor =
                FlightNumbers.SafeFactor(
                    result.MaxStaminaFactor);

            result.RecoveryFactor =
                FlightNumbers.SafeFactor(
                    result.RecoveryFactor);

            result.FlightSpeedFactor =
                FlightNumbers.SafeFactor(
                    result.FlightSpeedFactor);

            return result;
        }

        /// <summary>
        /// Все источники XML-поправок:
        /// раса -> черты -> детство -> взрослая предыстория.
        /// </summary>
        private static IEnumerable<FlightTraitExtension>
            GetExtensions(Pawn pawn)
        {
            // ----------------------------------------------------
            // НОВОЕ: КОЭФФИЦИЕНТЫ РАСЫ ИЗ ThingDef
            // ----------------------------------------------------

            FlightTraitExtension raceExtension =
                pawn.def.GetModExtension<FlightTraitExtension>();

            if (raceExtension != null)
                yield return raceExtension;

            // ----------------------------------------------------
            // ЧЕРТЫ ХАРАКТЕРА
            // ----------------------------------------------------

            if (pawn.story.traits != null)
            {
                List<Trait> traits =
                    pawn.story.traits.allTraits;

                for (int i = 0; i < traits.Count; i++)
                {
                    FlightTraitExtension extension =
                        GetExtension(traits[i]);

                    if (extension != null)
                        yield return extension;
                }
            }

            // ----------------------------------------------------
            // ДЕТСТВО
            // ----------------------------------------------------

            if (pawn.story.Childhood != null)
            {
                FlightTraitExtension extension =
                    pawn.story.Childhood
                        .GetModExtension<FlightTraitExtension>();

                if (extension != null)
                    yield return extension;
            }

            // ----------------------------------------------------
            // ВЗРОСЛАЯ ПРЕДЫСТОРИЯ
            // ----------------------------------------------------

            if (pawn.story.Adulthood != null)
            {
                FlightTraitExtension extension =
                    pawn.story.Adulthood
                        .GetModExtension<FlightTraitExtension>();

                if (extension != null)
                    yield return extension;
            }
        }

        internal static void ApplyXml(
            Pawn pawn,
            FlightParameter parameter,
            ref FlightAdjustment adjustment)
        {
            if (!IsEligible(pawn))
                return;

            foreach (
                FlightTraitExtension extension
                in GetExtensions(pawn))
            {
                switch (parameter)
                {
                    case FlightParameter.DurationTicks:
                        adjustment.Add +=
                            FlightNumbers.SafeAdd(
                                extension.maxStaminaAddTicks);

                        adjustment.Multiply *=
                            FlightNumbers.SafeFactor(
                                extension.maxStaminaFactor);
                        break;

                    case FlightParameter.RecoveryPerTick:
                        adjustment.Add +=
                            FlightNumbers.SafeAdd(
                                extension.recoveryAddPerTick);

                        adjustment.Multiply *=
                            FlightNumbers.SafeFactor(
                                extension.recoveryFactor);
                        break;

                    case FlightParameter.SpeedFactor:
                        adjustment.Add +=
                            FlightNumbers.SafeAdd(
                                extension.flightSpeedAdd);

                        adjustment.Multiply *=
                            FlightNumbers.SafeFactor(
                                extension.flightSpeedFactor);
                        break;

                    case FlightParameter.StaminaDrainPerSecond:
                        adjustment.Add +=
                            FlightNumbers.SafeAdd(
                                extension.staminaDrainAddPerSecond);

                        adjustment.Multiply *=
                            FlightNumbers.SafeMultiplier(
                                extension.staminaDrainFactor);
                        break;
                }
            }
        }
    }

    // ============================================================
    // ПРОВЕРКА ЧИСЛОВЫХ ЗНАЧЕНИЙ
    // ============================================================

    internal static class FlightNumbers
    {
        internal static bool Finite(float value)
        {
            return !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }

        internal static bool Positive(float value)
        {
            return Finite(value) && value > 0f;
        }

        internal static bool NonNegative(float value)
        {
            return Finite(value) && value >= 0f;
        }

        internal static bool Probability(float value)
        {
            return Finite(value) &&
                value >= 0f &&
                value <= 1f;
        }

        internal static float SafeFactor(float value)
        {
            return Positive(value) ? value : 1f;
        }

        internal static float SafeMultiplier(float value)
        {
            return NonNegative(value) ? value : 1f;
        }

        internal static float SafeAdd(float value)
        {
            return Finite(value) ? value : 0f;
        }
    }

    // ============================================================
    // ЕДИНСТВЕННАЯ ИНИЦИАЛИЗАЦИЯ HARMONY
    // ============================================================

    [StaticConstructorOnStartup]
    public static class FlightTraitsKernel
    {
        public const string HarmonyId =
            "flighttraits.poniesoftherim";

        static FlightTraitsKernel()
        {
            Harmony harmony = new Harmony(HarmonyId);

            // Длительность.
            Install(
                harmony,
                AccessTools.PropertyGetter(
                    typeof(CompPegasusFlightTimer),
                    "MaxFlightDurationTicks"),
                nameof(FlightKernelPatches.DurationPostfix),
                typeof(int));

            // Восстановление.
            Install(
                harmony,
                AccessTools.Method(
                    typeof(CompPegasusFlightTimer),
                    "RecoveryPerTick",
                    new[]
                    {
                        typeof(Pawn),
                        typeof(float)
                    }),
                nameof(FlightKernelPatches.RecoveryPostfix),
                typeof(float));

            // Расход.
            Install(
                harmony,
                AccessTools.PropertyGetter(
                    typeof(CompPegasusFlightTimer),
                    "StaminaDrainPerSecond"),
                nameof(FlightKernelPatches.DrainPostfix),
                typeof(float));

            // Скорость движения.
            Install(
                harmony,
                AccessTools.Method(
                    typeof(Pawn_PathFollower),
                    "CostToMoveIntoCell",
                    new[]
                    {
                        typeof(Pawn),
                        typeof(IntVec3)
                    }),
                nameof(FlightKernelPatches.MoveCostPostfix),
                typeof(float));

            // Отображаемая дальность перемещения в полёте.
            Install(
                harmony,
                AccessTools.Method(
                    typeof(PegasusFlightUtility),
                    "FlightCellsForStamina",
                    new[]
                    {
                        typeof(Pawn),
                        typeof(CompPegasusFlightTimer),
                        typeof(float)
                    }),
                nameof(FlightKernelPatches.FlightRangePostfix),
                typeof(float));

            // Фильтр генерации черт и предысторий.
            try
            {
                harmony.CreateClassProcessor(
                    typeof(FlightTraitsGenerationFilter))
                    .Patch();

                Log.Message(
                    "[FlightTraits] Фильтр генерации подключён.");
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FlightTraits] Ошибка фильтра генерации:\n"
                    + exception);
            }

            Log.Message(
                "[FlightTraits] Ядро и API инициализированы. "
                + "Поддерживаются XML-коэффициенты рас, "
                + "черт и предысторий.");
        }

        private static void Install(
            Harmony harmony,
            MethodInfo target,
            string postfixName,
            Type returnType)
        {
            if (target == null ||
                target.ReturnType != returnType)
            {
                Log.Error(
                    "[FlightTraits] Не найден совместимый метод для "
                    + postfixName
                    + ". Проверьте версию Ponies of the Rim.");
                return;
            }

            MethodInfo patch = AccessTools.Method(
                typeof(FlightKernelPatches),
                postfixName);

            if (patch == null)
            {
                Log.Error(
                    "[FlightTraits] Не найден собственный патч: "
                    + postfixName);
                return;
            }

            try
            {
                HarmonyMethod postfix =
                    new HarmonyMethod(patch);

                postfix.priority = Priority.Last;

                postfix.after = new[]
                {
                    "PoniesOfTheRim.Flying"
                };

                harmony.Patch(
                    target,
                    postfix: postfix);

                Log.Message(
                    "[FlightTraits] Подключён: " + postfixName);
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FlightTraits] Ошибка "
                    + postfixName
                    + ":\n"
                    + exception);
            }
        }
    }

    // ============================================================
    // ПАТЧИ ПАРАМЕТРОВ
    //
    // Все расчёты проходят через FlightAPI.
    // ============================================================

    internal static class FlightKernelPatches
    {
        internal static void DurationPostfix(
            CompPegasusFlightTimer __instance,
            ref int __result)
        {
            Pawn pawn = __instance.parent as Pawn;

            // Не разрешаем полёт, если оригинальный расчёт
            // уже дал нулевую длительность.
            if (__result <= 0 ||
                !FlightAPI.IsEligible(pawn))
            {
                return;
            }

            float adjusted = FlightAPI.Evaluate(
                pawn,
                FlightParameter.DurationTicks,
                __result,
                minimum: 1f);

            __result = (double)adjusted >= int.MaxValue
                ? int.MaxValue
                : Mathf.Max(
                    1,
                    Mathf.RoundToInt(adjusted));
        }

        internal static void RecoveryPostfix(
            Pawn __0,
            ref float __result)
        {
            if (__result <= 0f ||
                !FlightAPI.IsEligible(__0))
            {
                return;
            }

            __result = FlightAPI.Evaluate(
                __0,
                FlightParameter.RecoveryPerTick,
                __result);
        }

        internal static void DrainPostfix(
            CompPegasusFlightTimer __instance,
            ref float __result)
        {
            Pawn pawn = __instance.parent as Pawn;

            if (!FlightAPI.IsEligible(pawn))
                return;

            __result = FlightAPI.Evaluate(
                pawn,
                FlightParameter.StaminaDrainPerSecond,
                __result);
        }

        internal static void MoveCostPostfix(
            Pawn pawn,
            ref float __result)
        {
            if (!FlightAPI.IsFlying(pawn) ||
                !FlightNumbers.Positive(__result))
            {
                return;
            }

            float factor =
                FlightAPI.GetSpeedFactor(pawn);

            // Быстрее движение — меньше тиков на клетку.
            __result = Mathf.Max(
                1f,
                __result / factor);
        }

        internal static void FlightRangePostfix(
            Pawn pawn,
            ref float __result)
        {
            if (!FlightAPI.IsEligible(pawn) ||
                !FlightNumbers.Positive(__result) ||
                __result == float.MaxValue)
            {
                return;
            }

            float factor =
                FlightAPI.GetSpeedFactor(pawn);

            double adjusted =
                (double)__result * factor;

            __result = adjusted >= float.MaxValue
                ? float.MaxValue
                : (float)adjusted;
        }
    }

    // ============================================================
    // ФИЛЬТР ГЕНЕРАЦИИ
    //
    // Лётные черты и предыстории не выбираются обычным
    // генератором для неподходящих пешек.
    // ============================================================

    [HarmonyPatch]
    public static class FlightTraitsGenerationFilter
    {
        [ThreadStatic]
        private static Pawn currentTraitPawn;

        [ThreadStatic]
        private static Pawn currentBioPawn;

        private static bool IsFlightDef(Def def)
        {
            return def != null &&
                def.GetModExtension<FlightTraitExtension>() != null;
        }

        [HarmonyPatch(
            typeof(PawnGenerator),
            nameof(PawnGenerator.GenerateTraitsFor))]
        [HarmonyPrefix]
        private static void BeforeGenerateTraitsFor(
            Pawn pawn,
            out Pawn __state)
        {
            __state = currentTraitPawn;
            currentTraitPawn = pawn;
        }

        [HarmonyPatch(
            typeof(PawnGenerator),
            nameof(PawnGenerator.GenerateTraitsFor))]
        [HarmonyFinalizer]
        private static Exception AfterGenerateTraitsFor(
            Exception __exception,
            Pawn __state)
        {
            currentTraitPawn = __state;
            return __exception;
        }

        [HarmonyPatch(
            typeof(TraitDef),
            nameof(TraitDef.GetGenderSpecificCommonality))]
        [HarmonyPostfix]
        private static void FilterTraitWeight(
            TraitDef __instance,
            ref float __result)
        {
            if (currentTraitPawn != null &&
                IsFlightDef(__instance) &&
                !FlightAPI.IsEligible(currentTraitPawn))
            {
                __result = 0f;
            }
        }

        [HarmonyPatch(
            typeof(PawnBioAndNameGenerator),
            nameof(
                PawnBioAndNameGenerator
                    .GiveAppropriateBioAndNameTo))]
        [HarmonyPrefix]
        private static void BeforeGenerateBio(
            Pawn pawn,
            out Pawn __state)
        {
            __state = currentBioPawn;
            currentBioPawn = pawn;
        }

        [HarmonyPatch(
            typeof(PawnBioAndNameGenerator),
            nameof(
                PawnBioAndNameGenerator
                    .GiveAppropriateBioAndNameTo))]
        [HarmonyFinalizer]
        private static Exception AfterGenerateBio(
            Exception __exception,
            Pawn __state)
        {
            currentBioPawn = __state;
            return __exception;
        }

        [HarmonyPatch(
            typeof(BackstoryCategoryFilter),
            nameof(BackstoryCategoryFilter.Matches),
            new[] { typeof(BackstoryDef) })]
        [HarmonyPostfix]
        private static void FilterShuffledBackstory(
            BackstoryDef __0,
            ref bool __result)
        {
            if (__result &&
                currentBioPawn != null &&
                IsFlightDef(__0) &&
                !FlightAPI.IsEligible(currentBioPawn))
            {
                __result = false;
            }
        }

        [HarmonyPatch(
            typeof(PawnBioAndNameGenerator),
            "IsBioUseable")]
        [HarmonyPostfix]
        private static void FilterSolidBio(
            PawnBio bio,
            ref bool __result)
        {
            if (__result &&
                currentBioPawn != null &&
                !FlightAPI.IsEligible(currentBioPawn) &&
                (IsFlightDef(bio.childhood) ||
                 IsFlightDef(bio.adulthood)))
            {
                __result = false;
            }
        }
    }
}