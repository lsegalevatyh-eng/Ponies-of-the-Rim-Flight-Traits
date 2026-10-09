using System;
using System.Collections.Generic;

using HarmonyLib;
using PoniesOfTheRim.Flying;
using RimWorld;
using UnityEngine;
using Verse;

namespace FlightTraits
{
    // XML-настройки тренировочного компонента.
    public sealed class CompProperties_PegasusFlightTraining : CompProperties
    {
        public int trainingLevelsMax = 6;
        public int atrophyLevelsMax = 6;

        public float trainingStaminaBonus = 0.05f;
        public float trainingSpeedBonus = 0.01f;
        public float atrophyStaminaPenalty = 0.05f;
        public float atrophySpeedPenalty = 0.01f;

        // Не ежедневное требование. Если явный порог не задан,
        // баллы за окно = cyclesPerDayRequired * trainingDaysRequired.
        public int cyclesPerDayRequired = 2;
        public int trainingDaysRequired = 5;
        public int atrophyDaysRequired = 15;
        public int trainingPointsRequired = -1;

        public CompProperties_PegasusFlightTraining()
        {
            compClass = typeof(CompPegasusFlightTraining);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
                yield return error;

            if (trainingLevelsMax < 0)
                yield return "trainingLevelsMax must be >= 0.";
            if (atrophyLevelsMax < 0)
                yield return "atrophyLevelsMax must be >= 0.";
            if (trainingDaysRequired <= 0)
                yield return "trainingDaysRequired must be > 0.";
            if (atrophyDaysRequired <= 0)
                yield return "atrophyDaysRequired must be > 0.";
            if (cyclesPerDayRequired <= 0 && trainingPointsRequired <= 0)
                yield return "Set cyclesPerDayRequired > 0 or trainingPointsRequired > 0.";
            if (!ValidBonus(trainingStaminaBonus))
                yield return "Invalid trainingStaminaBonus.";
            if (!ValidBonus(trainingSpeedBonus))
                yield return "Invalid trainingSpeedBonus.";
            if (!ValidBonus(atrophyStaminaPenalty))
                yield return "Invalid atrophyStaminaPenalty.";
            if (!ValidBonus(atrophySpeedPenalty))
                yield return "Invalid atrophySpeedPenalty.";
        }

        private static bool ValidBonus(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }

    public sealed class CompPegasusFlightTraining : ThingComp
    {
        private const float FullStaminaThreshold = 0.999999f;

        private int trainingLevel;
        private int atrophyLevel;
        private int trainingPoints;

        private int trainingWindowStartTick = -1;
        private int atrophyPeriodStartTick = -1;
        private int lastObservedTick = -1;
        private bool fullCycleInProgress;

        public CompProperties_PegasusFlightTraining Props =>
            (CompProperties_PegasusFlightTraining)props;

        public Pawn Pawn => parent as Pawn;
        public int TrainingLevel => trainingLevel;
        public int AtrophyLevel => atrophyLevel;
        public int TrainingPoints => trainingPoints;

        // Это свойство используется и расчётом, и отображением здоровья.
        public int RequiredTrainingPoints
        {
            get
            {
                if (Props.trainingPointsRequired > 0)
                    return Props.trainingPointsRequired;

                long points = (long)Math.Max(1, Props.cyclesPerDayRequired)
                    * Math.Max(1, Props.trainingDaysRequired);

                return (int)Math.Min(int.MaxValue, points);
            }
        }

        private long TrainingWindowTicks =>
            (long)Math.Max(1, Props.trainingDaysRequired) * GenDate.TicksPerDay;

        private long AtrophyPeriodTicks =>
            (long)Math.Max(1, Props.atrophyDaysRequired) * GenDate.TicksPerDay;

        // Свойства для FlightTraitsTrainingHealth.cs.
        // Только чтение: они не изменяют игровое состояние.
        public float TrainingProgress =>
            Mathf.Clamp01(trainingPoints / (float)RequiredTrainingPoints);

        public float AtrophyProgress
        {
            get
            {
                if (atrophyPeriodStartTick < 0 || Find.TickManager == null)
                    return 0f;

                long elapsed = Math.Max(0L,
                    (long)Find.TickManager.TicksGame - atrophyPeriodStartTick);

                return Mathf.Clamp01((float)(elapsed / (double)AtrophyPeriodTicks));
            }
        }

        public float TrainingDaysRemaining
        {
            get
            {
                if (trainingWindowStartTick < 0 || Find.TickManager == null)
                    return Math.Max(1, Props.trainingDaysRequired);

                long remaining = Math.Max(0L,
                    (long)trainingWindowStartTick + TrainingWindowTicks
                    - Find.TickManager.TicksGame);

                return remaining / (float)GenDate.TicksPerDay;
            }
        }

        public float GetStaminaFactor()
        {
            return Mathf.Max(0.01f,
                1f + trainingLevel * Props.trainingStaminaBonus
                - atrophyLevel * Props.atrophyStaminaPenalty);
        }

        public float GetSpeedFactor()
        {
            return Mathf.Max(0.01f,
                1f + trainingLevel * Props.trainingSpeedBonus
                - atrophyLevel * Props.atrophySpeedPenalty);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            Scribe_Values.Look(ref trainingLevel, "flightTrainingLevel", 0);
            Scribe_Values.Look(ref atrophyLevel, "flightAtrophyLevel", 0);
            Scribe_Values.Look(ref trainingPoints, "flightTrainingPoints", 0);
            Scribe_Values.Look(ref trainingWindowStartTick,
                "flightTrainingWindowStartTick", -1);
            Scribe_Values.Look(ref atrophyPeriodStartTick,
                "flightAtrophyPeriodStartTick", -1);
            Scribe_Values.Look(ref lastObservedTick,
                "flightTrainingLastObservedTick", -1);
            Scribe_Values.Look(ref fullCycleInProgress,
                "flightTrainingFullCycleInProgress", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                NormalizeLevels();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (Find.TickManager != null)
                ProcessTime(Find.TickManager.TicksGame);
        }

        public override void CompTick()
        {
            base.CompTick();

            if (Pawn == null || !Pawn.Spawned || Pawn.Dead || Find.TickManager == null)
                return;

            ProcessTime(Find.TickManager.TicksGame);
        }

        // Имя и сигнатура сохранены для патча отображения здоровья.
        public bool TrainingWindowActive => trainingWindowStartTick >= 0;

        internal void ProcessTime(int now)
        {
            NormalizeLevels();
            if (atrophyPeriodStartTick < 0) atrophyPeriodStartTick = now;
            if (lastObservedTick < 0) lastObservedTick = now;

            if (now < lastObservedTick)
            {
                trainingWindowStartTick = -1;
                atrophyPeriodStartTick = now;
                trainingPoints = 0;
                fullCycleInProgress = false;
            }

            if (trainingPoints == 0) trainingWindowStartTick = -1;
            if (trainingWindowStartTick < 0 && TrainingProgress > 0.01f)
                trainingWindowStartTick = now;

            // Сначала обрабатываем завершившиеся сроки атрофии.
            while ((long)atrophyPeriodStartTick + AtrophyPeriodTicks <= now)
            {
                ApplyAtrophyStep();
                atrophyPeriodStartTick = (int)((long)atrophyPeriodStartTick + AtrophyPeriodTicks);
            }

            // Уже накопленные 100% в старом сохранении также применяются.
            if (trainingPoints >= RequiredTrainingPoints)
            {
                ApplyTrainingStep();
                trainingPoints = 0;
                trainingWindowStartTick = -1;
            }
            else if (trainingWindowStartTick >= 0 &&
                (long)trainingWindowStartTick + TrainingWindowTicks <= now)
            {
                // Не успели набрать порог: окно закрывается без повышения.
                trainingPoints = 0;
                trainingWindowStartTick = -1;
            }
            lastObservedTick = now;
        }

        private void ApplyTrainingStep()
        {
            // Сначала тренировка погашает один уровень атрофии.
            if (atrophyLevel > 0)
            {
                atrophyLevel--;
                return;
            }

            if (trainingLevel < Math.Max(0, Props.trainingLevelsMax))
                trainingLevel++;
        }

        private void ApplyAtrophyStep()
        {
            // Сначала атрофия погашает один уровень тренировки.
            if (trainingLevel > 0)
            {
                trainingLevel--;
                return;
            }

            if (atrophyLevel < Math.Max(0, Props.atrophyLevelsMax))
                atrophyLevel++;
        }

        private void NormalizeLevels()
        {
            trainingLevel = Mathf.Clamp(trainingLevel,
                0, Math.Max(0, Props.trainingLevelsMax));
            atrophyLevel = Mathf.Clamp(atrophyLevel,
                0, Math.Max(0, Props.atrophyLevelsMax));

            int cancel = Math.Min(trainingLevel, atrophyLevel);
            trainingLevel -= cancel;
            atrophyLevel -= cancel;
            trainingPoints = Math.Max(0, trainingPoints);
        }

        internal void ObserveTimerTick(
            float staminaBefore,
            float staminaAfter,
            bool flightWasEnabled)
        {
            Pawn pawn = Pawn;

            if (pawn == null || !pawn.Spawned || pawn.Dead || Find.TickManager == null)
                return;

            if (float.IsNaN(staminaBefore) || float.IsNaN(staminaAfter)
                || float.IsInfinity(staminaBefore) || float.IsInfinity(staminaAfter))
                return;

            int now = Find.TickManager.TicksGame;
            ProcessTime(now);

            bool spentStamina = flightWasEnabled && staminaAfter < staminaBefore;
            bool recoveredStamina = staminaAfter > staminaBefore;

            // Восстановление прерывает незаконченный полный цикл.
            if (recoveredStamina)
                fullCycleInProgress = false;

            if (!spentStamina)
                return;

            // Любой реальный расход в полёте сбрасывает срок атрофии.
            atrophyPeriodStartTick = now;

            if (staminaBefore >= FullStaminaThreshold)
                fullCycleInProgress = true;

            if (staminaBefore > 0f && staminaAfter <= 0f)
                NotifyFlightCycleComplete();
        }

        public void NotifyFlightCycleComplete()
        {
            Pawn pawn = Pawn;

            if (!fullCycleInProgress || pawn == null || pawn.Dead
                || Find.TickManager == null)
                return;

            CompPegasusFlightTimer timer = pawn.TryGetComp<CompPegasusFlightTimer>();

            if (timer == null || timer.CurrentStaminaPercent > 0f)
                return;

            ProcessTime(Find.TickManager.TicksGame);
            fullCycleInProgress = false;

            if (trainingPoints < int.MaxValue)
                trainingPoints++;

            // Запуск окна по первому прогрессу выше 1%.
            // При достижении порога шаг применяется немедленно.
            ProcessTime(Find.TickManager.TicksGame);
        }

        public override string CompInspectStringExtra()
        {
            return null;
        }
    }

    // Подключение коэффициентов к существующему API ядра.
    internal sealed class FlightTrainingModifier : IFlightModifier
    {
        public string Id => "flighttraits.training-and-atrophy";
        public int Priority => 200;

        public void Modify(
            FlightContext context,
            FlightParameter parameter,
            ref FlightAdjustment adjustment)
        {
            if (!context.IsEligible || context.Pawn == null)
                return;

            if (parameter != FlightParameter.DurationTicks
                && parameter != FlightParameter.SpeedFactor)
                return;

            CompPegasusFlightTraining training =
                context.Pawn.TryGetComp<CompPegasusFlightTraining>();

            if (training == null)
                return;

            if (parameter == FlightParameter.DurationTicks)
                adjustment.MultiplyBy(training.GetStaminaFactor());
            else
                adjustment.MultiplyBy(training.GetSpeedFactor());
        }
    }

    // Снимок выносливости до и после тика оригинального таймера.
    internal static class FlightTrainingTimerPatch
    {
        internal struct TickState
        {
            public CompPegasusFlightTraining Training;
            public float StaminaBefore;
            public bool FlightWasEnabled;
        }

        internal static void Prefix(
            CompPegasusFlightTimer __instance,
            out TickState __state)
        {
            __state = default;

            Pawn pawn = __instance.parent as Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Dead)
                return;

            CompPegasusFlightTraining training =
                pawn.TryGetComp<CompPegasusFlightTraining>();
            if (training == null)
                return;

            CompPegasusFlightToggle toggle =
                pawn.TryGetComp<CompPegasusFlightToggle>();

            __state = new TickState
            {
                Training = training,
                StaminaBefore = __instance.CurrentStaminaPercent,
                FlightWasEnabled = toggle != null && toggle.FlightEnabled
            };
        }

        internal static void Postfix(
            CompPegasusFlightTimer __instance,
            TickState __state)
        {
            if (__state.Training == null)
                return;

            __state.Training.ObserveTimerTick(
                __state.StaminaBefore,
                __instance.CurrentStaminaPercent,
                __state.FlightWasEnabled);
        }
    }

    [StaticConstructorOnStartup]
    public static class FlightTrainingBootstrap
    {
        private const string HarmonyId = "flighttraits.training";

        static FlightTrainingBootstrap()
        {
            FlightAPI.Register(new FlightTrainingModifier());

            try
            {
                var target = AccessTools.DeclaredMethod(
                    typeof(CompPegasusFlightTimer),
                    nameof(CompPegasusFlightTimer.CompTick),
                    Type.EmptyTypes);

                if (target == null)
                {
                    Log.Error(
                        "[FlightTraits Training] Не найден CompPegasusFlightTimer.CompTick. "
                        + "Проверьте версию Ponies of the Rim.");
                    return;
                }

                Harmony harmony = new Harmony(HarmonyId);

                HarmonyMethod prefix = new HarmonyMethod(
                    AccessTools.Method(typeof(FlightTrainingTimerPatch),
                        nameof(FlightTrainingTimerPatch.Prefix)));

                HarmonyMethod postfix = new HarmonyMethod(
                    AccessTools.Method(typeof(FlightTrainingTimerPatch),
                        nameof(FlightTrainingTimerPatch.Postfix)));

                prefix.priority = Priority.First;
                postfix.priority = Priority.Last;

                harmony.Patch(target, prefix: prefix, postfix: postfix);

                Log.Message(
                    "[FlightTraits Training] Тренировка и атрофия подключены.");
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FlightTraits Training] Ошибка подключения таймера:\n" + exception);
            }
        }
    }
}
