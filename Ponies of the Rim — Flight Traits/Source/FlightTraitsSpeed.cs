using System;
using HarmonyLib;
using PoniesOfTheRim.Flying;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FlightTraits
{
    [StaticConstructorOnStartup]
    public static class FlightTraitsSpeedBootstrap
    {
        static FlightTraitsSpeedBootstrap()
        {
            var harmony = new Harmony(
                "flighttraits.poniesoftherim.speed"
            );

            try
            {
                var target = AccessTools.Method(
                    typeof(Pawn_PathFollower),
                    "CostToMoveIntoCell",
                    new[] { typeof(Pawn), typeof(IntVec3) }
                );

                if (target == null ||
                    target.ReturnType != typeof(float))
                {
                    Log.Error(
                        "[FlightTraits] Не найден ожидаемый " +
                        "CostToMoveIntoCell с результатом float."
                    );
                    return;
                }

                var postfix = new HarmonyMethod(
                    AccessTools.Method(
                        typeof(FlightTraitsSpeedPatches),
                        nameof(FlightTraitsSpeedPatches.MoveCostPostfix)
                    )
                );

                // Применяем бонус после обработки
                // препятствий оригинальным модом.
                postfix.after = new[]
                {
                    "PoniesOfTheRim.Flying"
                };
                postfix.priority = Priority.Last;

                harmony.Patch(target, postfix: postfix);

                var rangeTarget = AccessTools.Method(
                    typeof(PegasusFlightUtility),
                    "FlightCellsForStamina",
                    new[]
                    {
                        typeof(Pawn),
                        typeof(CompPegasusFlightTimer),
                        typeof(float)
                    }
                );

                if (rangeTarget != null &&
                    rangeTarget.ReturnType == typeof(float))
                {
                    harmony.Patch(
                        rangeTarget,
                        postfix: new HarmonyMethod(
                            AccessTools.Method(
                                typeof(FlightTraitsSpeedPatches),
                                nameof(
                                    FlightTraitsSpeedPatches.RangePostfix
                                )
                            )
                        )
                    );
                }
                else
                {
                    Log.Warning(
                        "[FlightTraits] Скорость подключена, " +
                        "но поправка отображаемой дальности " +
                        "не подключена: метод не найден."
                    );
                }

                Log.Message(
                    "[FlightTraits] Патч скорости полёта подключён."
                );
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[FlightTraits] Ошибка подключения скорости:\n"
                    + exception
                );
            }
        }
    }

    public static class FlightTraitsSpeedPatches
    {
        public static void MoveCostPostfix(
            Pawn pawn,
            ref float __result)
        {
            if (pawn == null ||
                !PegasusFlightUtility.IsPegasusConstantFlight(pawn))
            {
                return;
            }

            float factor =
                FlightTraitUtility.GetModifiers(pawn)
                    .FlightSpeedFactor;

            if (float.IsNaN(__result) ||
                float.IsInfinity(__result) ||
                __result <= 0f)
            {
                return;
            }

            // Стоимость движения измеряется временем:
            // больше скорость -> меньше времени на клетку.
            __result = Mathf.Max(1f, __result / factor);
        }

        public static void RangePostfix(
            Pawn pawn,
            ref float __result)
        {
            if (!FlightTraitUtility.IsEligible(pawn))
                return;

            if (__result <= 0f ||
                __result == float.MaxValue ||
                float.IsNaN(__result) ||
                float.IsInfinity(__result))
            {
                return;
            }

            float factor =
                FlightTraitUtility.GetModifiers(pawn)
                    .FlightSpeedFactor;

            double adjusted = (double)__result * factor;

            __result = adjusted >= float.MaxValue
                ? float.MaxValue
                : (float)adjusted;
        }
    }
}