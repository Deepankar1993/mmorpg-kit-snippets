﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerARPG
{

    public partial class PlayerCharacterController : BasePlayerCharacterController
    {

        public GameObject recticle;
        public bool uisOpen;
        public LayerMask TabTargetIgnoreLayers;

        public float TargetDistance
        {
            get
            {
                return lockAttackTargetDistance;
            }
        }

        protected TabTargeting _targeting;

        public TabTargeting Targeting
        {
            get
            {
                if (!_targeting)
                {
                    _targeting = BasePlayerCharacterController.OwningCharacter.gameObject.GetComponentInChildren<TabTargeting>();
                    if (!_targeting)
                    {
                        GameObject go = new GameObject();
                        _targeting = go.AddComponent<TabTargeting>();
                        go.transform.SetParent(BasePlayerCharacterController.OwningCharacter.gameObject.transform);
                    }
                }
                return _targeting;
            }
        }

        public virtual void Activate()
        {
            isFollowingTarget = true;
        }

        protected void TabTargetUpdateWASDAttack()
        {
            destination = null;

            if (Targeting.SelectedTarget && !((Targeting.SelectedTarget?.GetComponent<BaseCharacterEntity>())?.IsHideOrDead() ?? false))
            {
                // Set target, then attack later when moved nearby target
                SetTarget(Targeting.SelectedTarget.GetComponent<BaseCharacterEntity>(), TargetActionType.Attack, false);
                isFollowingTarget = true;
            }
        }

        public virtual void TabTargetUpdateInput()
        {
            bool isFocusInputField = GenericUtils.IsFocusInputField() || UIElementUtils.IsUIElementActive();
            bool isPointerOverUIObject = CacheUISceneGameplay.IsPointerOverUIObject();
            if (CacheGameplayCameraControls != null)
            {
                CacheGameplayCameraControls.updateRotationX = false;
                CacheGameplayCameraControls.updateRotationY = false;
                CacheGameplayCameraControls.updateRotation = !isFocusInputField && !isPointerOverUIObject && InputManager.GetButton("CameraRotate");
                CacheGameplayCameraControls.updateZoom = !isFocusInputField && !isPointerOverUIObject;
            }

            if (isFocusInputField)
                return;

            if (PlayerCharacterEntity.IsDead())
                return;

            // If it's building something, don't allow to activate NPC/Warp/Pickup Item
            if (ConstructingBuildingEntity == null)
            {
                Targeting.HandleTargeting();

                if (InputManager.GetButtonDown("PickUpItem"))
                {
                    PickUpItem();
                }
                if (InputManager.GetButtonDown("Reload"))
                {
                    ReloadAmmo();
                }
                if (InputManager.GetButtonDown("ExitVehicle"))
                {
                    PlayerCharacterEntity.CallServerExitVehicle();
                }
                if (InputManager.GetButtonDown("SwitchEquipWeaponSet"))
                {
                    PlayerCharacterEntity.CallServerSwitchEquipWeaponSet((byte)(PlayerCharacterEntity.EquipWeaponSet + 1));
                }
                if (InputManager.GetButtonDown("Sprint"))
                {
                    isSprinting = !isSprinting;
                }
                // Auto reload
                if (PlayerCharacterEntity.EquipWeapons.rightHand.IsAmmoEmpty() ||
                    PlayerCharacterEntity.EquipWeapons.leftHand.IsAmmoEmpty())
                {
                    ReloadAmmo();
                }
            }
            // Update enemy detecting radius to attack distance
            EnemyEntityDetector.detectingRadius = Mathf.Max(PlayerCharacterEntity.GetAttackDistance(false), wasdClearTargetDistance);
            // Update inputs
            UpdateQueuedSkill();
            TabTargetUpdatePointClickInput();
            UpdateWASDInput();
            // Set sprinting state
            PlayerCharacterEntity.SetExtraMovement(isSprinting ? ExtraMovementState.IsSprinting : ExtraMovementState.None);
        }


        public virtual void TabTargetUpdateWASDInput()
        {
            if (controllerMode == PlayerCharacterControllerMode.PointClick)
                return;

            // If mobile platforms, don't receive input raw to make it smooth
            bool raw = !InputManager.useMobileInputOnNonMobile && !Application.isMobilePlatform;
            Vector3 moveDirection = GetMoveDirection(InputManager.GetAxis("Horizontal", raw), InputManager.GetAxis("Vertical", raw));
            moveDirection.Normalize();

            // Move
            if (moveDirection.sqrMagnitude > 0f)
            {
                HideNpcDialog();
                ClearQueueUsingSkill();
                destination = null;
                isFollowingTarget = false;
                if (Targeting.SelectedTarget != null && Vector3.Distance(CacheTransform.position, Targeting.SelectedTarget.transform.position) >= wasdClearTargetDistance)
                {
                    Targeting.UnTarget(Targeting.SelectedTarget);
                }
                if (Targeting.PotentialTarget != null && Vector3.Distance(CacheTransform.position, Targeting.PotentialTarget.transform.position) >= wasdClearTargetDistance)
                {
                    Targeting.UnHighlightPotentialTarget();
                }
                if (!PlayerCharacterEntity.IsPlayingActionAnimation())
                    PlayerCharacterEntity.SetLookRotation(Quaternion.LookRotation(moveDirection));
            }

            // Always forward
            MovementState movementState = MovementState.Forward;
            if (InputManager.GetButtonDown("Jump"))
                movementState |= MovementState.IsJump;
            PlayerCharacterEntity.KeyMovement(moveDirection, movementState);
        }
        protected virtual void PickUpItem()
        {
            targetItemDrop = null;
            if (ItemDropEntityDetector.itemDrops.Count > 0)
                targetItemDrop = ItemDropEntityDetector.itemDrops[0];
            if (targetItemDrop != null)
                PlayerCharacterEntity.CallServerPickupItem(targetItemDrop.ObjectId);
        }

        public virtual void TabTargetUpdatePointClickInput()
        {
            if (controllerMode == PlayerCharacterControllerMode.WASD)
                return;

            // If it's building something, not allow point click movement
            if (ConstructingBuildingEntity != null)
                return;

            // If it's aiming skills, not allow point click movement
            if (UICharacterHotkeys.UsingHotkey != null)
                return;

            getMouseDown = Input.GetMouseButtonDown(0);
            getMouseUp = Input.GetMouseButtonUp(0);
            getMouse = Input.GetMouseButton(0);

            if (getMouseDown)
            {
                isMouseDragOrHoldOrOverUI = false;
                mouseDownTime = Time.unscaledTime;
                mouseDownPosition = Input.mousePosition;
            }
            // Read inputs
            isPointerOverUI = CacheUISceneGameplay.IsPointerOverUIObject();
            isMouseDragDetected = (Input.mousePosition - mouseDownPosition).sqrMagnitude > DETECT_MOUSE_DRAG_DISTANCE_SQUARED;
            isMouseHoldDetected = Time.unscaledTime - mouseDownTime > DETECT_MOUSE_HOLD_DURATION;
            isMouseHoldAndNotDrag = !isMouseDragDetected && isMouseHoldDetected;
            if (!isMouseDragOrHoldOrOverUI && (isMouseDragDetected || isMouseHoldDetected || isPointerOverUI))
            {
                // Detected mouse dragging or hold on an UIs
                isMouseDragOrHoldOrOverUI = true;
            }
            // Will set move target when pointer isn't point on an UIs 
            if (!isPointerOverUI && (getMouse || getMouseUp))
            {
                Targeting.UnTarget(Targeting.SelectedTarget);
                Targeting.UnHighlightPotentialTarget();
                didActionOnTarget = false;
                // Prepare temp variables
                Transform tempTransform;
                Vector3 tempVector3;
                bool tempHasMapPosition = false;
                Vector3 tempMapPosition = Vector3.zero;
                BuildingMaterial tempBuildingMaterial;
                // If mouse up while cursor point to target (character, item, npc and so on)
                bool mouseUpOnTarget = getMouseUp && !isMouseDragOrHoldOrOverUI;
                int tempCount = FindClickObjects(out tempVector3);
                for (int tempCounter = 0; tempCounter < tempCount; ++tempCounter)
                {
                    tempTransform = physicFunctions.GetRaycastTransform(tempCounter);
                    // When holding on target, or already enter edit building mode
                    if (isMouseHoldAndNotDrag)
                    {
                        targetBuilding = null;
                        tempBuildingMaterial = tempTransform.GetComponent<BuildingMaterial>();
                        if (tempBuildingMaterial != null)
                            targetBuilding = tempBuildingMaterial.BuildingEntity;
                        if (targetBuilding && !targetBuilding.IsDead())
                        {
                            Targeting.Target(targetBuilding.gameObject);
                            break;
                        }
                    }
                    else if (mouseUpOnTarget)
                    {
                        Targeting.Target(tempTransform.gameObject);
                    } // End mouseUpOnTarget
                }
                // When clicked on map (Not touch any game entity)
                // - Clear selected target to hide selected entity UIs
                // - Set target position to position where mouse clicked
                if (tempHasMapPosition)
                {
                    targetPosition = tempMapPosition;
                }
                // When clicked on map (any non-collider position)
                // tempVector3 is come from FindClickObjects()
                // - Clear character target to make character stop doing actions
                // - Clear selected target to hide selected entity UIs
                // - Set target position to position where mouse clicked
                if (CurrentGameInstance.DimensionType == DimensionType.Dimension2D && mouseUpOnTarget && tempCount == 0)
                {
                    ClearTarget();
                    tempVector3.z = 0;
                    targetPosition = tempVector3;
                }

                // Found ground position
                if (targetPosition.HasValue)
                {
                    // Close NPC dialog, when target changes
                    HideNpcDialog();
                    ClearQueueUsingSkill();
                    isFollowingTarget = false;
                    if (PlayerCharacterEntity.IsPlayingActionAnimation())
                    {
                        if (pointClickInterruptCastingSkill)
                            PlayerCharacterEntity.CallServerSkillCastingInterrupt();
                    }
                    else
                    {
                        OnPointClickOnGround(targetPosition.Value);
                    }
                }
            }
        }

        protected virtual void TabTargetUpdateQueuedSkill()
        {
            if (PlayerCharacterEntity.IsDead())
            {
                ClearQueueUsingSkill();
                return;
            }
            if (queueUsingSkill.skill == null || queueUsingSkill.level <= 0)
                return;
            if (PlayerCharacterEntity.IsPlayingActionAnimation())
                return;
            destination = null;
            BaseSkill skill = queueUsingSkill.skill;
            Vector3? aimPosition = queueUsingSkill.aimPosition;
            Debug.Log(Targeting.PotentialTarget + " " + Targeting.SelectedTarget);
            BaseGameEntity target = (Targeting.PotentialTarget ?? Targeting.SelectedTarget)?.GetComponent<BaseGameEntity>();
            if (skill.HasCustomAimControls())
            {
                // Target not required, use skill immediately
                TurnCharacterToPosition(aimPosition.Value);
                RequestUsePendingSkill();
                isFollowingTarget = false;
                return;
            }

            if (skill.IsAttack())
            {
                // Let's stick to tab targeting instead of finding a random entity
                if (target != null && target is BaseCharacterEntity)
                {
                    SetTarget(target, TargetActionType.UseSkill, false);
                    RequestUsePendingSkill();
                    isFollowingTarget = false;
                }
                else
                {
                    ClearQueueUsingSkill();
                    isFollowingTarget = false;
                }
            }
            else
            {
                // Not attack skill, so use skill immediately
                if (skill.RequiredTarget())
                {
                    // Set target, then use skill later when moved nearby target
                    if (target != null && target is BaseCharacterEntity)
                    {
                        RequestUsePendingSkill();
                    }
                    else
                    {
                        ClearQueueUsingSkill();
                        isFollowingTarget = false;
                    }
                }
                else
                {

                    // Target not required, use skill immediately
                    RequestUsePendingSkill();
                    isFollowingTarget = false;
                }
            }
        }


        public virtual void HandleTargetChange(Transform tempTransform)
        {
            if (tempTransform)
            {
                targetPlayer = tempTransform.GetComponent<BasePlayerCharacterEntity>();
                targetMonster = tempTransform.GetComponent<BaseMonsterCharacterEntity>();
                targetNpc = tempTransform.GetComponent<NpcEntity>();
                targetItemDrop = tempTransform.GetComponent<ItemDropEntity>();
                targetHarvestable = tempTransform.GetComponent<HarvestableEntity>();
                targetBuilding = null;
                targetVehicle = tempTransform.GetComponent<VehicleEntity>();
                if (targetPlayer)
                {
                    // Found activating entity as player character entity
                    if (!targetPlayer.IsHideOrDead() && !targetPlayer.IsAlly(PlayerCharacterEntity))
                        SetTarget(targetPlayer, TargetActionType.Attack);
                    else
                        SetTarget(targetPlayer, TargetActionType.Activate);
                }
                else if (targetMonster && !targetMonster.IsHideOrDead())
                {
                    // Found activating entity as monster character entity
                    SetTarget(targetMonster, TargetActionType.Attack);
                }
                else if (targetNpc)
                {
                    // Found activating entity as npc entity
                    SetTarget(targetNpc, TargetActionType.Activate);
                }
                else if (targetItemDrop)
                {
                    // Found activating entity as item drop entity
                    SetTarget(targetItemDrop, TargetActionType.Activate);
                }
                else if (targetHarvestable && !targetHarvestable.IsDead())
                {
                    // Found activating entity as harvestable entity
                    SetTarget(targetHarvestable, TargetActionType.Attack);
                }
                else if (targetBuilding && !targetBuilding.IsDead() && targetBuilding.Activatable)
                {
                    // Found activating entity as building entity
                    SetTarget(targetBuilding, TargetActionType.Activate);
                }
                else if (targetVehicle)
                {
                    // Found activating entity as vehicle entity
                    SetTarget(targetVehicle, TargetActionType.Activate);
                }
                else
                {
                    SetTarget(null, TargetActionType.Attack);
                    isFollowingTarget = false;
                }
            }
            else
            {
                SetTarget(null, TargetActionType.Attack);
                isFollowingTarget = false;
            }
        }
    }
}