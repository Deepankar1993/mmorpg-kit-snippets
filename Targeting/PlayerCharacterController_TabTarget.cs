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
            if (Targeting.SelectedTarget?.GetComponent<BaseCharacterEntity>() != null)
                this.targetActionType = TargetActionType.Attack;
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

        public virtual void TabTargetUpdateTarget()
        {
            
            Debug.Log("Casting: " + PlayerCharacterEntity.GetCastingTargetEntity()?.gameObject.transform.position);
            Debug.Log("SubTarget: " + PlayerCharacterEntity.GetSubTargetEntity()?.gameObject.transform.position);
            //Debug.Log("MainTarget: " + PlayerCharacterEntity.GetTargetEntity()?.gameObject.transform.position);
            PlayerCharacterEntity.SetSubTarget(Targeting.PotentialTarget != null ? Targeting.PotentialTarget.GetComponent<BaseGameEntity>() : null);
            PlayerCharacterEntity.SetCastingTarget(Targeting.castingOnTarget != null ? Targeting.castingOnTarget.GetComponent<BaseGameEntity>() : null);
            PlayerCharacterEntity.SetTargetEntity(Targeting.SelectedTarget != null ? Targeting.SelectedTarget.GetComponent<BaseGameEntity>() : null);

            GameObject target = (Targeting.PotentialTarget ?? Targeting.SelectedTarget);
            SelectedEntity = Targeting.SelectedTarget != null ? Targeting.SelectedTarget.GetComponent<BaseGameEntity>() : null;
            BaseGameEntity targetForUI = target != null ? target.GetComponent<BaseGameEntity>() : null;
            TargetEntity = SelectedEntity;
            CacheUISceneGameplay.SetTargetEntity(targetForUI);
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
            TabTargetUpdateQueuedSkill();
            TabTargetUpdatePointClickInput();
            TabTargetUpdateWASDInput();
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
            GameObject targetObj = Targeting.PotentialTarget ?? Targeting.SelectedTarget;
            BaseGameEntity target = targetObj ? targetObj.GetComponent<BaseGameEntity>() : null;
            Vector3? aimPosition = queueUsingSkill.aimPosition;
            if (skill.HasCustomAimControls())
            {
                // Target not required, use skill immediately
                TurnCharacterToPosition(aimPosition.Value);
                RequestUsePendingSkill();
                isFollowingTarget = false;
                return;
            }

            if (skill.IsAttack() || skill.RequiredTarget())
            {
                // Let's stick to tab targeting instead of finding a random entity
                if (target != null && target is BaseCharacterEntity)
                {
                    Targeting.castingOnTarget = target.gameObject;
                    if (Targeting.SelectedTarget == null)
                        Targeting.Target(Targeting.castingOnTarget);
                    if (Targeting.castingOnTarget == Targeting.PotentialTarget)
                        Targeting.UnHighlightPotentialTarget();
                    TabTargetUpdateTarget();
                    TurnCharacterToPosition(target.transform.position);
                    RequestUsePendingSkill();
                    return;
                }
                ClearQueueUsingSkill();
                return;
            }
            // Target not required, use skill immediately
            RequestUsePendingSkill();
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
                return;
            }
            isFollowingTarget = false;
        }

        public void TabTargetUpdateFollowTarget()
        {
            if (!isFollowingTarget)
                return;

            if (TryGetAttackingEntity(out targetDamageable))
            {
                if (targetDamageable.IsHideOrDead())
                {
                    ClearQueueUsingSkill();
                    PlayerCharacterEntity.StopMove();
                    ClearTarget();
                    Targeting.UnTarget(Targeting.SelectedTarget);
                    return;
                }
                float attackDistance = 0f;
                float attackFov = 0f;
                GetAttackDistanceAndFov(isLeftHandAttacking, out attackDistance, out attackFov);
                AttackOrMoveToEntity(targetDamageable, attackDistance, CurrentGameInstance.characterLayer.Mask);
            }
            else if (TryGetUsingSkillEntity(out targetDamageable))
            {
                if (queueUsingSkill.skill.IsAttack() && targetDamageable.IsHideOrDead())
                {
                    ClearQueueUsingSkill();
                    PlayerCharacterEntity.StopMove();
                    ClearTarget();
                    Targeting.UnTarget(Targeting.SelectedTarget);
                    return;
                }
                float castDistance = 0f;
                float castFov = 0f;
                GetUseSkillDistanceAndFov(isLeftHandAttacking, out castDistance, out castFov);
                UseSkillOrMoveToEntity(targetDamageable, castDistance);
            }
            else if (TryGetDoActionEntity(out targetPlayer))
            {
                DoActionOrMoveToEntity(targetPlayer, CurrentGameInstance.conversationDistance, () =>
                {
                    // TODO: Do something
                });
            }
            else if (TryGetDoActionEntity(out targetNpc))
            {
                DoActionOrMoveToEntity(targetNpc, CurrentGameInstance.conversationDistance, () =>
                {
                    if (!didActionOnTarget)
                    {
                        didActionOnTarget = true;
                        PlayerCharacterEntity.CallServerNpcActivate(targetNpc.ObjectId);
                    }
                });
            }
            else if (TryGetDoActionEntity(out targetItemDrop))
            {
                DoActionOrMoveToEntity(targetItemDrop, CurrentGameInstance.pickUpItemDistance, () =>
                {
                    PlayerCharacterEntity.CallServerPickupItem(targetItemDrop.ObjectId);
                    ClearTarget();
                });
            }
            else if (TryGetDoActionEntity(out targetBuilding, TargetActionType.Activate))
            {
                DoActionOrMoveToEntity(targetBuilding, CurrentGameInstance.conversationDistance, () =>
                {
                    if (!didActionOnTarget)
                    {
                        didActionOnTarget = true;
                        ActivateBuilding(targetBuilding);
                    }
                });
            }
            else if (TryGetDoActionEntity(out targetBuilding, TargetActionType.ViewOptions))
            {
                DoActionOrMoveToEntity(targetBuilding, CurrentGameInstance.conversationDistance, () =>
                {
                    if (!didActionOnTarget)
                    {
                        didActionOnTarget = true;
                        ShowCurrentBuildingDialog();
                    }
                });
            }
            else if (TryGetDoActionEntity(out targetVehicle))
            {
                DoActionOrMoveToEntity(targetVehicle, CurrentGameInstance.conversationDistance, () =>
                {
                    PlayerCharacterEntity.CallServerEnterVehicle(targetVehicle.ObjectId);
                    ClearTarget();
                    Targeting.UnTarget(Targeting.SelectedTarget);
                });
            }
        }
    }
}