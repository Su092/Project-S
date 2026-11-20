using UnityEngine;
using UnityEngine.InputSystem;
namespace FoundryDelivery {
[RequireComponent(typeof(CharacterController))]
public class FoundryWalkthrough:MonoBehaviour{
 public Camera view; public float speed=6; float pitch,vertical;
 void Start(){Cursor.lockState=CursorLockMode.Locked;gameObject.layer=LayerMask.NameToLayer("FoundryPlayer");Physics.IgnoreLayerCollision(gameObject.layer,LayerMask.NameToLayer("FoundryShot"),true);}
 void Update(){
 var k=Keyboard.current;var m=Mouse.current;if(k==null||m==null)return;
 if(k.escapeKey.wasPressedThisFrame)Cursor.lockState=CursorLockMode.None;
 if(m.rightButton.wasPressedThisFrame)Cursor.lockState=CursorLockMode.Locked;
 if(Cursor.lockState!=CursorLockMode.Locked)return;
 Vector2 delta=m.delta.ReadValue()*.09f;transform.Rotate(0,delta.x,0);pitch=Mathf.Clamp(pitch-delta.y,-85,85);view.transform.localRotation=Quaternion.Euler(pitch,0,0);
 float x=(k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),z=(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0);
 var cc=GetComponent<CharacterController>();if(cc.isGrounded&&vertical<0)vertical=-2;
 vertical-=20*Time.deltaTime;
 cc.Move((transform.TransformDirection(Vector3.ClampMagnitude(new Vector3(x,0,z),1))*speed*(k.leftShiftKey.isPressed?1.5f:1)+Vector3.up*vertical)*Time.deltaTime);
 if(m.leftButton.wasPressedThisFrame){
 int mask=~((1<<LayerMask.NameToLayer("FoundryMovement"))|(1<<gameObject.layer));
 if(Physics.Raycast(view.transform.position,view.transform.forward,out var hit,150,mask)){
 Debug.DrawLine(view.transform.position,hit.point,Color.cyan,3);
 var mark=GameObject.CreatePrimitive(PrimitiveType.Sphere);mark.name="QA_ShotHit";mark.transform.position=hit.point;mark.transform.localScale=Vector3.one*.08f;Destroy(mark.GetComponent<Collider>());Destroy(mark,2);
 }
 }
 }
 void OnGUI(){GUI.Label(new Rect(20,20,720,25),"FOUNDRY INSPECTION | WASD move | Shift run | Mouse look | LMB shot probe | Esc unlock");GUI.Label(new Rect(Screen.width/2-4,Screen.height/2-12,16,24),"+");}
}
}
