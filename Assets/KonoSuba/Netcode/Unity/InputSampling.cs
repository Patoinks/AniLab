using AnimLab.Netcode.Sim;
using UnityEngine;

namespace AnimLab.Netcode.Unity
{
    public static class InputSampling
    {
        public static PlayerInput Quantize(Vector3 wish,float yaw,Buttons buttons,bool targeted=false)
        {
            wish.y=0;
            int angle=Mathf.RoundToInt(Mathf.Atan2(wish.x,wish.z)*256/(2*Mathf.PI))&255;
            return new PlayerInput { MoveAngle=(byte)angle,Magnitude=(byte)Mathf.RoundToInt(Mathf.Clamp01(wish.magnitude)*15),
                LookYaw=(ushort)(Mathf.RoundToInt(Mathf.Repeat(yaw,360)*1024/360)&1023),Buttons=buttons,Targeted=targeted };
        }
        public static PlayerInput SecondKeyboard(Transform actor,Transform opponent)
        {
            var wish=new Vector3((Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.LeftArrow)?1:0),0,
                (Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.DownArrow)?1:0));
            float cameraYaw=Camera.main!=null?Camera.main.transform.eulerAngles.y:0;
            wish=Quaternion.Euler(0,cameraYaw,0)*Vector3.ClampMagnitude(wish,1);
            Vector3 aim=opponent.position-actor.position;
            float yaw=Mathf.Atan2(aim.x,aim.z)*Mathf.Rad2Deg;
            Buttons buttons=(Input.GetKeyDown(KeyCode.J)?Buttons.Attack:0)|(Input.GetKey(KeyCode.K)?Buttons.Guard:0)|
                (Input.GetKeyDown(KeyCode.RightShift)?Buttons.Jump:0)|(Input.GetKeyDown(KeyCode.RightControl)?Buttons.Roll:0)|
                (Input.GetKey(KeyCode.Return)?Buttons.Run:0);
            return Quantize(wish,yaw,buttons,true);
        }
    }
}
