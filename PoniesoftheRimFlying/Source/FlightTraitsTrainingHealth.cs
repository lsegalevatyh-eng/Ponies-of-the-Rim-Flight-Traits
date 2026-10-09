using System;

using HarmonyLib;
using UnityEngine;
using Verse;

namespace FlightTraits
{
    // ============================================================
    // СОСТОЯНИЕ ВО ВКЛАДКЕ ЗДОРОВЬЯ
    //
    // Не рассчитывает бонусы и не управляет тренировкой.
    // Только показывает данные компонента.
    // ============================================================

    public sealed class Hediff_FlightTrainingStatus : Hediff
    {
        private CompPegasusFlightTraining Training =>
            pawn?.TryGetComp<CompPegasusFlightTraining>();

        // Это постоянный индикатор состояния.
        // Не удаляется автоматически при нулевой severity.
        public override bool ShouldRemove => false;

        public override string LabelInBrackets
        {
            get
            {
                CompPegasusFlightTraining training = Training;

                if (training == null)
                    return null;

                string stage;

                if (training.TrainingLevel > 0)
                {
                    stage =
                        "тренировка "
                        + training.TrainingLevel;
                }
                else if (training.AtrophyLevel > 0)
                {
                    stage =
                        "атрофия "
                        + training.AtrophyLevel;
                }
                else
                {
                    stage = "обычное состояние";
                }

                string trainingPercent =
                    training.TrainingProgress.ToStringPercent("F0");

                string atrophyPercent =
                    training.AtrophyProgress.ToStringPercent("F0");

                return
                    stage
                    + "; тренировка: "
                    + trainingPercent
                    + "; атрофия: "
                    + atrophyPercent;
            }
        }

        public override string TipStringExtra
        {
            get
            {
                CompPegasusFlightTraining training = Training;

                if (training == null)
                    return base.TipStringExtra;

                string text =
                    "Уровень тренировки: "
                    + training.TrainingLevel
                    + "/"
                    + Math.Max(0, training.Props.trainingLevelsMax)

                    + "\nУровень атрофии: "
                    + training.AtrophyLevel
                    + "/"
                    + Math.Max(0, training.Props.atrophyLevelsMax)

                    + "\n\nПрогресс тренировки: "
                    + training.TrainingProgress.ToStringPercent("F0")

                    + "\nПолных циклов за текущее окно: "
                    + training.TrainingPoints
                    + "/"
                    + training.RequiredTrainingPoints

                    + "\nДо конца тренировочного окна: "
                    + training.TrainingDaysRemaining.ToString("F1")
                    + " д."

                    + "\n\nПрогресс к атрофии: "
                    + training.AtrophyProgress.ToStringPercent("F0")

                    + "\nЛюбой расход выносливости во время полёта "
                    + "сбрасывает прогресс к атрофии."

                    + "\n\nВыносливость: ×"
                    + training.GetStaminaFactor().ToString("F2")

                    + "\nСкорость полёта: ×"
                    + training.GetSpeedFactor().ToString("F2")

                    + "\n\n100% тренировки означает, что баллы набраны. "
                    + "Шаг тренировки происходит после окончания окна."

                    + "\nПри росте тренировки сначала уменьшается атрофия. "
                    + "При росте атрофии сначала уменьшается тренировка.";

                string original = base.TipStringExtra;

                return string.IsNullOrEmpty(original)
                    ? text
                    : original + "\n" + text;
            }
        }
    }

    // ============================================================
    // СИНХРОНИЗАЦИЯ УРОВНЯ КОМПОНЕНТА СО СТАДИЕЙ HEDIFF
    //
    // Severity используется только для выбора стадии:
    //
    //  0 = атрофия VI
    //  1 = атрофия V
    //  ...
    //  5 = атрофия I
    //  6 = обычное состояние
    //  7 = тренировка I
    //  ...
    // 12 = тренировка VI
    //
    // Проценты берутся напрямую из компонента,
    // а не из severity.
    // ============================================================

    internal static class FlightTrainingHealthSync
    {
        private const string StatusDefName =
            "FlightTraits_FlightTrainingStatus";

        private static HediffDef statusDef;
        private static bool missingDefReported;

        internal static void Sync(
            CompPegasusFlightTraining training)
        {
            Pawn pawn = training.Pawn;

            if (pawn == null ||
                !pawn.Spawned ||
                pawn.Dead ||
                pawn.health?.hediffSet == null)
            {
                return;
            }

            if (statusDef == null)
            {
                statusDef = DefDatabase<HediffDef>.GetNamed(
                    StatusDefName,
                    errorOnFail: false);
            }

            if (statusDef == null)
            {
                if (!missingDefReported)
                {
                    missingDefReported = true;

                    Log.Error(
                        "[FlightTraits Training] "
                        + "Не найден HediffDef "
                        + StatusDefName
                        + ". Добавьте XML состояния крыльев.");
                }

                return;
            }

            Hediff status =
                pawn.health.hediffSet.GetFirstHediffOfDef(statusDef);

            float targetSeverity = Mathf.Clamp(
                6f
                + training.TrainingLevel
                - training.AtrophyLevel,
                0f,
                12f);

            if (status == null)
            {
                status = HediffMaker.MakeHediff(
                    statusDef,
                    pawn);

                status.Severity = targetSeverity;
                pawn.health.AddHediff(status);
            }
            else if (status.Severity != targetSeverity)
            {
                status.Severity = targetSeverity;
            }
        }
    }

    // ============================================================
    // ПОДКЛЮЧЕНИЕ ОТОБРАЖЕНИЯ
    //
    // ProcessTime уже вызывается компонентом при появлении
    // пешки и во время симуляции.
    //
    // Не меняем состояние из методов отрисовки интерфейса.
    // ============================================================

    internal static class FlightTrainingHealthPatch
    {
        internal static void Postfix(
            CompPegasusFlightTraining __instance)
        {
            FlightTrainingHealthSync.Sync(__instance);
        }
    }

    [StaticConstructorOnStartup]
    public static class FlightTrainingHealthBootstrap
    {
        static FlightTrainingHealthBootstrap()
        {
            try
            {
                var target = AccessTools.DeclaredMethod(
                    typeof(CompPegasusFlightTraining),
                    "ProcessTime",
                    new[] { typeof(int) });

                if (target == null)
                {
                    Log.Error(
                        "[FlightTraits Training] "
                        + "Не найден ProcessTime для подключения "
                        + "отображения во вкладке здоровья.");
                    return;
                }

                var postfix = AccessTools.Method(
                    typeof(FlightTrainingHealthPatch),
                    nameof(FlightTrainingHealthPatch.Postfix));

                Harmony harmony = new Harmony(
                    "flighttraits.training.health");

                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(postfix));

                Log.Message(
                    "[FlightTraits Training] "
                    + "Отображение состояния крыльев "
                    + "во вкладке здоровья подключено.");
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FlightTraits Training] "
                    + "Ошибка подключения отображения здоровья:\n"
                    + exception);
            }
        }
    }
}