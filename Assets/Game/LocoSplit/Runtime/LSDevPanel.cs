// In-game dev tools panel (F1 toggles). Sliders for twist limits, layer switches, sword grip and robe shape.
using UnityEngine;
using UnityEngine.InputSystem;

namespace LocoSplit
{
    public class LSDevPanel : MonoBehaviour
    {
        public LSPlayer player;
        public LSRig rig;
        public LSCamera camRig;
        public LSRobeCloth robe;
        bool robeVisible = true;
        public bool show = true;

        public static bool MouseOverPanel;
        Rect win = new Rect(10, 10, 330, 640);
        Vector2 scroll;

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) show = !show;
            if (Mouse.current != null)
            {
                var mp = Mouse.current.position.ReadValue();
                MouseOverPanel = show && win.Contains(new Vector2(mp.x, Screen.height - mp.y));
            }
        }

        void OnGUI()
        {
            if (!show) return;
            win = GUI.Window(7311, win, Draw, "대검 이동 테스트 (F1)");
        }

        float Slider(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} {v:0.##}", GUILayout.Width(140));
            v = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return v;
        }

        void Draw(int id)
        {
            scroll = GUILayout.BeginScrollView(scroll);
            if (player)
            {
                GUILayout.Label($"허리 비틀림 {player.Twist:0}°  하체 이동 ({player.LocalMove.x:0.0}, {player.LocalMove.y:0.0})");
                player.faceCursor = GUILayout.Toggle(player.faceCursor, "몸 전체가 커서 방향 (끄면 상하체 분리)");
                player.backSpeedMul = Slider("뒷걸음 속도 배율", player.backSpeedMul, 0.3f, 1f);
                player.strafeSpeedMul = Slider("옆걸음 속도 배율", player.strafeSpeedMul, 0.3f, 1f);
                player.faceTurnSpeed = Slider("몸 회전 속도°/s", player.faceTurnSpeed, 180, 1800);
                player.upperLayerOn = GUILayout.Toggle(player.upperLayerOn, "상체 레이어 (Avatar Mask)");
                bool full = GUILayout.Toggle(player.attackMode == AttackMode.FullBody, "공격 시 하체도 사용 (전신 공격)");
                player.attackMode = full ? AttackMode.FullBody : AttackMode.UpperOnly;
                player.maxTwist = Slider("최대 비틀림°", player.maxTwist, 0, 120);
                player.twistWeight = Slider("비틀림 적용", player.twistWeight, 0, 1);
                player.stanceYawOffset = Slider("자세 보정°", player.stanceYawOffset, -90, 90);
                player.hipFollowSpeed = Slider("이동 중 하체 회전", player.hipFollowSpeed, 0, 720);
                player.idleTurnThreshold = Slider("제자리 회전 시작°", player.idleTurnThreshold, 0, 120);
                player.moveSpeed = Slider("이동 속도", player.moveSpeed, 1, 6);
            }
            if (rig)
            {
                GUILayout.Space(6);
                GUILayout.Label("대검 잡는 위치");
                if (GUILayout.Button("양손 그립 자동 맞추기 (대기 자세에서)")) rig.Regrip();
                rig.swordPos.x = Slider("pos x", rig.swordPos.x, -0.3f, 0.3f);
                rig.swordPos.y = Slider("pos y", rig.swordPos.y, -0.3f, 0.3f);
                rig.swordPos.z = Slider("pos z", rig.swordPos.z, -0.3f, 0.3f);
                rig.swordEuler.x = Slider("rot x", rig.swordEuler.x, -180, 180);
                rig.swordEuler.y = Slider("rot y", rig.swordEuler.y, -180, 180);
                rig.swordEuler.z = Slider("rot z", rig.swordEuler.z, -180, 180);
                rig.swordScale = Slider("크기", rig.swordScale, 0.3f, 2f);
                GUILayout.Space(6);
                rig.hatPos.y = Slider("갓 높이", rig.hatPos.y, -0.1f, 0.3f);
                rig.hatPos.z = Slider("갓 앞뒤", rig.hatPos.z, -0.15f, 0.15f);
                rig.hatScale = Slider("갓 크기", rig.hatScale, 0.5f, 1.5f);
            }
            if (robe)
            {
                GUILayout.Space(6);
                bool on = GUILayout.Toggle(robeVisible, "도포 (Cloth, 다리 충돌)");
                if (on != robeVisible) { robeVisible = on; robe.SetVisible(on); }
                float s0 = robe.maxSwing, s1 = robe.stiffness, s2 = robe.damping;
                robe.maxSwing = Slider("흔들림 한계", robe.maxSwing, 0.05f, 0.8f);
                robe.stiffness = Slider("뻣뻣함", robe.stiffness, 0.1f, 1f);
                robe.damping = Slider("감쇠", robe.damping, 0f, 0.6f);
                if (s0 != robe.maxSwing || s1 != robe.stiffness || s2 != robe.damping) robe.ApplyClothSettings();
            }
            if (camRig)
            {
                GUILayout.Space(6);
                camRig.distance = Slider("카메라 거리", camRig.distance, 3, 20);
                camRig.pitch = Slider("카메라 각도", camRig.pitch, 20, 89);
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
    }
}
