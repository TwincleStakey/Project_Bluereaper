using System;
using System.Reflection;
using BlueReaper.HitBox;
using UnityEditor;
using UnityEngine;

namespace BlueReaper.Data.Editor
{
    public static class HitWindowChecks
    {
        [MenuItem("Tools/BlueReaper/Check Hit Window")]
        public static void Run()
        {
            var type = typeof(Hurtbox).Assembly.GetType("BlueReaper.HitBox.HitWindow", true);
            const BindingFlags members = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
            var window = Activator.CreateInstance(type, true);
            int Slice(float elapsed, bool repeat = true) =>
                (int)type.GetMethod("Slice", members).Invoke(null, new object[] { elapsed, 1f, repeat, 0.3f });
            bool Claim(object part, float elapsed, bool repeat = true) =>
                (bool)type.GetMethod("Claim", members).Invoke(window, new[] { part, (object)Slice(elapsed, repeat) });

            var head = new object();
            var body = new object();
            Must(Slice(-0.01f) == -1 && Slice(0f) == 0 && Slice(0.3f) == 1 &&
                Slice(0.9f) == 3 && Slice(0.99f) == 3 && Slice(1f) == -1, "Window and slice boundaries");
            Must(Claim(head, 0.59f) && !Claim(head, 0.595f) && Claim(body, 0.595f) &&
                Claim(head, 0.61f) && !Claim(head, 1f), "Late arrival, independent parts and shared slices");
            type.GetMethod("Reset", members).Invoke(window, null);
            Must(Claim(head, 0.61f, false) && !Claim(head, 0.99f, false), "One hit after a reset with repeat disabled");
            Debug.Log("HIT WINDOW PASS: boundary, late arrival, independent parts, repeat off and reset");
            var area = ScriptableObject.CreateInstance<AreaAttackPatternData>();
            try
            {
                area.HitStartSeconds = 0.5f;
                area.HitDurationSeconds = 0.25f;
                Must(area.GetValidationError() == null &&
                    !EnemyPatternEditor.IsHighlighted(area, 0.49f) &&
                    EnemyPatternEditor.IsHighlighted(area, 0.5f) &&
                    EnemyPatternEditor.IsHighlighted(area, 0.74f) &&
                    !EnemyPatternEditor.IsHighlighted(area, 0.75f), "Preview must respect start and duration without repeat");
                area.HitDurationSeconds = 0f;
                Must(area.GetValidationError() != null, "Duration must be positive even without repeat");
                Debug.Log("AREA DURATION PASS: required duration and preview boundaries with repeat off");
            }
            finally { UnityEngine.Object.DestroyImmediate(area); }
        }

        private static void Must(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
