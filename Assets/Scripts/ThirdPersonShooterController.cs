using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonShooterController : MonoBehaviour
{
    Transform playerTransform;
    Animator animator;
    Transform cameraTransform;
    CharacterController characterController;

    public enum PlayerPosture
    {
        Crouch,
        Stand,
        Midair //滞空
    };
    [HideInInspector]
    public PlayerPosture playerPosture = PlayerPosture.Stand;

    float crouchThreshold = 0f;
    float standThreshold = 1f;
    float midairThreshold = 2.1f; // 要大于2，不然会反复抖动

    public enum LocomotionState
    {
        Idle,
        Walk,
        Run
    };
    [HideInInspector]
    public LocomotionState locomotionState = LocomotionState.Idle;

    public enum ArmState
    {
        Normal,
        Aim
    };
    [HideInInspector]
    public ArmState armState = ArmState.Normal;

    float crouchSpeed = 1.5f;
    float walkSpeed = 2.5f;
    float runSpeed = 5.5f;

    Vector2 moveInput;//接收移动输入
    bool isRunning;
    bool isCrouch;
    bool isAiming;
    bool isJumping;

    // 保存相关参数的哈希值（性能更高）对应保存Animator中的Parameter
    int postureHash;
    int moveSpeedHash;
    int turnSpeedHash;
    int jumpSpeedHash;

    int feetTweenHash;

    Vector3 playerMovement = Vector3.zero;// 玩家实际移动的方向

    public float gravity = -9.8f; // 重力

    float VerticalVelocity; //垂直方向速度

    //public float jumpVelocity = 5f; //向上跳跃的速度

    public float maxJumpHigh = 1f; //玩家可跳跃的最大高度



    // 对跳跃bug的解除
    Vector3 lastVelOnGround;

    static readonly int CACHE_SIZE = 3; // 🌟 C#语法：readonly关键字，与const的区别只在于，const一开始必须赋值。它们同样是赋值了之后就不能改了。
    Vector3[] velCache = new Vector3[CACHE_SIZE]; //保存离地前3帧的移动速度
    int currentChacheIndex = 0;
    private Vector3 averageVel = Vector3.zero;

    float fallMultiplier = 1.5f; //下降的加速度要比上升的多多少。

    // 弃用cc的isGrounded
    bool isGrounded;

    float groundCheckOffset = 0.5f; //射线偏移量

    // Start is called before the first frame update
    void Start()
    {
        playerTransform = transform; //提升访问效率
        animator = GetComponent<Animator>();
        characterController = GetComponent<CharacterController>();
        cameraTransform = Camera.main.transform;

        postureHash = Animator.StringToHash("玩家姿态");
        moveSpeedHash = Animator.StringToHash("移动速度");
        turnSpeedHash = Animator.StringToHash("转弯速度");
        jumpSpeedHash = Animator.StringToHash("垂直速度");
        feetTweenHash = Animator.StringToHash("左右脚");
        Cursor.lockState = CursorLockMode.Locked;
    }

    // Update is called once per frame
    void Update()
    {
        CheckGround();
        CaculateGravity();
        Jump();
        CaculateInputDirection();
        SwitchPlayerStates();
        SetupAnimator();
    }

    #region 输入相关
    public void GetMoveInput(InputAction.CallbackContext ctx)
    {
        moveInput = ctx.ReadValue<Vector2>();
    }

    public void GetRunInput(InputAction.CallbackContext ctx)
    {
        isRunning = ctx.ReadValueAsButton();
    }

    public void GetCrouchInput(InputAction.CallbackContext ctx)
    {
        isCrouch = ctx.ReadValueAsButton();
    }

    public void GetAimInput(InputAction.CallbackContext ctx)
    {
        isAiming = ctx.ReadValueAsButton();
    }

    public void GetJumpInput(InputAction.CallbackContext ctx)
    {
        isJumping = ctx.ReadValueAsButton();
    }
    #endregion
    void SwitchPlayerStates()
    {
        if(!isGrounded)
        {
            playerPosture = PlayerPosture.Midair;
        }
        else if(isCrouch) //要为else if是因为，如果不在地面不能蹲。
        {
            playerPosture = PlayerPosture.Crouch;
        }
        else
        {
            playerPosture = PlayerPosture.Stand;
        }
        if(moveInput.magnitude == 0) // 向量的长度，及(x,y)到(0,0)的距离
        {
            locomotionState = LocomotionState.Idle;
        }
        else if(!isRunning)
        {
            locomotionState = LocomotionState.Walk;
        }
        else
        {
            locomotionState = LocomotionState.Run;
        }
        if(isAiming)
        {
            armState = ArmState.Aim;
        }
        else
        {
            armState = ArmState.Normal;
        }
    }

    void CheckGround()
    {
        if(Physics.SphereCast(playerTransform.position + (Vector3.up * groundCheckOffset), characterController.radius, Vector3.down, out RaycastHit hit, groundCheckOffset - characterController.radius + 2 * characterController.skinWidth))
        {
            isGrounded = true;
        }
        else
        {
            isGrounded = false;
        }
    }

    void Jump()
    {
        if(isGrounded && isJumping)
        {
            //VerticalVelocity = jumpVelocity;
            VerticalVelocity = Mathf.Sqrt(-2 * gravity * maxJumpHigh);
            float feetTween = Random.Range(-1f,1f);
            animator.SetFloat(feetTweenHash, feetTween);
        }
    }

    void CaculateGravity()
    {
        if(isGrounded) // isGrounded的检查必须要求要有向下的加速度，不然不会正常工作，所以我们要把角色按在地上，看下面一行注释掉的是原来的
        {
            //VerticalVelocity = 0;
            //Debug.Log(characterController.isGrounded);
            VerticalVelocity = gravity * Time.deltaTime; // 先初始化为0
            return;
        }
        else //下落
        {
            if (VerticalVelocity <= 0)
            {
                VerticalVelocity += gravity * fallMultiplier * Time.deltaTime;
            }
            else
            {
                VerticalVelocity += gravity * Time.deltaTime; // 重力加速度公式
            } 
        }
    }


    void CaculateInputDirection()
    {
        Vector3 camForwardProjection = new Vector3(cameraTransform.forward.x, 0, cameraTransform.forward.z).normalized;
        playerMovement = camForwardProjection * moveInput.y + cameraTransform.right * moveInput.x;
        playerMovement = playerTransform.InverseTransformVector(playerMovement);
    }

    void SetupAnimator()
    {
        if(playerPosture == PlayerPosture.Stand)
        {
            animator.SetFloat(postureHash, standThreshold, 0.1f, Time.deltaTime);
            switch(locomotionState)
            {
                case LocomotionState.Idle:
                    animator.SetFloat(moveSpeedHash, 0, 0.1f, Time.deltaTime);
                    break;
                case LocomotionState.Walk:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * walkSpeed, 0.1f, Time.deltaTime);
                    break;
                case LocomotionState.Run:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * runSpeed, 0.1f, Time.deltaTime);
                    break;
            }
        }
        else if(playerPosture == PlayerPosture.Crouch)
        {
            animator.SetFloat(postureHash, crouchThreshold, 0.1f, Time.deltaTime);
            switch(locomotionState)
            {
                case LocomotionState.Idle:
                    animator.SetFloat(moveSpeedHash, 0, 0.1f, Time.deltaTime);
                    break;
                default:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * crouchSpeed, 0.1f, Time.deltaTime);
                    break;
            }
        }
        else if(playerPosture == PlayerPosture.Midair)
        {
            // animator.SetFloat(postureHash, midairThreshold, 0.1f, Time.deltaTime);
            animator.SetFloat(postureHash, midairThreshold);
            // animator.SetFloat(jumpSpeedHash, VerticalVelocity, 0.1f, Time.deltaTime);
            animator.SetFloat(jumpSpeedHash, VerticalVelocity);
        }
        if(armState == ArmState.Normal)
        {
            float rad = Mathf.Atan2(playerMovement.x, playerMovement.z); // Atan2返回rad = arctan(参数1，参数2) = a弧度 = b角度
            animator.SetFloat(turnSpeedHash, rad, 0.1f, Time.deltaTime); // 用了CharacterController转向会失效
            // 转向速度加快
            playerTransform.Rotate(0, rad * 200 * Time.deltaTime, 0f);
        }
    }

    /// <summary>
    /// 用在Update里,返回包括当前帧的前三帧的平均速度。
    /// </summary>
    /// <param name="newVel">当前这一帧的速度</param>
    /// <returns>起跳前前三帧的速度</returns>
    Vector3 AverageVel(Vector3 newVel)
    {
        velCache[currentChacheIndex] = newVel;
        currentChacheIndex++;
        currentChacheIndex %= CACHE_SIZE; //可以保证currentChacheIndex一直在0-2。防止溢出
        Vector3 average = Vector3.zero;
        foreach (Vector3 vel in velCache)
        {
            average += vel;
        }

        return average / CACHE_SIZE;
    }

    private void OnAnimatorMove()
    {
        // 重力
        if(playerPosture != PlayerPosture.Midair)
        {
            Vector3 playerDeltaMovement = animator.deltaPosition;
            playerDeltaMovement.y = VerticalVelocity * Time.deltaTime; //老师这里用的是=
            characterController.Move(playerDeltaMovement);
            // 计算跳跃前前三帧的速度。（这个一定要放在非跳跃状态计算存好，你跳后怎么获取的到跳前前三帧速度呢？就靠这个了）
            averageVel = AverageVel(animator.velocity);
        }
        else
        {
            // 错误方法❌ 沿用地面速度，比如使用最后一帧的速度（容易受影响，比如地形，或者一些玩家奇怪的操作）
            // 正确方法✅ 沿用地面速度，使用前几帧速度的平均值，来作为跳跃的速度，可以尽量避免影响
            averageVel.y = VerticalVelocity;
            Vector3 playerDeltaMovement = averageVel * Time.deltaTime;
            playerDeltaMovement.y = VerticalVelocity * Time.deltaTime; //老师这里用的是=
            characterController.Move(playerDeltaMovement);
        }
        
    }
}
