using Godot;
using System;
using System.Collections.Generic;

public class Player : KinematicBody2D {
	[Signal]
	public delegate void SendInfoToQuestNPC(Player p, NPC questNPC);
	
	[Signal]
	public delegate void CutsceneEnd(NPC questNPC);
	
	[Signal]
	public delegate void SlideInNotebookController();
	
	[Signal]
	public delegate void OpenNotebook();
	
	//Player FSM
	enum PlayerStates { IDLE, WALKING, RUNNING, BLOCKED };
	PlayerStates CurrentState = PlayerStates.IDLE;
	
	private bool NotebookOpen = false;
	private PlayerStates PrevState = PlayerStates.IDLE;
	
	// Cutscene state
	
	private bool isCutsceneConv = false;
	private ColorRect FadeIn;
	
	[Export]
	public int WalkSpeed = 100; //Pixels per second
	[Export]
	public int RunSpeed = 150;
	[Export]
	public float RunTime = 3.0f; // Seconds
	[Export]
	public bool isCutscene;
	
	//Empirical acceleration and friction amounts
	private const int ACC = 950;
	private const int FRIC = 1000;
	
	private int Speed = 0;
	private bool RunRequest = false;
	private float RunCooldown = 0.0f;
	
	public Vector2 Velocity = Vector2.Zero;
	private Vector2 InputVec = Vector2.Zero;
	
	private AnimationPlayer animation;
	private AnimationTree animationTree; 
	private AnimationNodeStateMachinePlayback animationState;
	
	private List<NPC> subs = new List<NPC>();
	private int nSubs = 0;
	
	// Returns whether or not the quest giver is in the sub list
	private bool QuestGiverIsSubbed() {
		foreach(var sub in subs) {
			if(sub.isQuestNPC) {
				return true;
			}
		}
		return false;
	}
	
	private void HandleMovement(float delta) {
		Speed = CurrentState == PlayerStates.RUNNING ? RunSpeed : WalkSpeed;
		
		//Update velocity
		if(InputVec == Vector2.Zero) {
			Velocity = Velocity.MoveToward(Vector2.Zero, FRIC * delta);
		} else {
			//Set blend positions for animation
			animationTree.Set("parameters/Walk/blend_position", InputVec);
			animationTree.Set("parameters/Run/blend_position", InputVec);
			animationTree.Set("parameters/Idle/blend_position", InputVec);
			Velocity = Velocity.MoveToward(InputVec * Speed, ACC * delta);
		}
	}
	
	// Walk up to the quest giver and interact
	private void HandleCutscene(float delta) {
		if(isCutsceneConv) {
			if(Input.IsActionJustPressed("ui_interact")) {
				NearestSub()._Notify(this);
			}
		} else {
			InputVec.x = 0.0f;
			InputVec.y = -1.0f;
			if(subs.Count != 0 && QuestGiverIsSubbed()) {
				var nearestNPC = NearestSub();
				if(nearestNPC.isQuestNPC) {
					InputVec.y = 0.0f;
					nearestNPC._Notify(this);
				}
			} 
		}
		InputVec = InputVec.Normalized();
		HandleMovement(delta);
	}
	
	/**
	 * @brief Checks for player input and updates its velocity accordingly
	 * @param delta, the time elapsed since the last update
	 */
	private void HandleInput(float delta) {
		if(CurrentState != PlayerStates.BLOCKED) {
			//Handle movement
			InputVec.x = Input.GetActionStrength("ui_right") - Input.GetActionStrength("ui_left");
			InputVec.y = Input.GetActionStrength("ui_down") - Input.GetActionStrength("ui_up");
			InputVec = InputVec.Normalized();
			
			//Check for sprint
			RunRequest = Input.IsActionPressed("ui_shift");
			HandleMovement(delta);
		} else {
			InputVec = Vector2.Zero;
			Velocity = Vector2.Zero;
		}
		
		//Check for interaction
		if(subs.Count != 0) {
			if(Input.IsActionJustPressed("ui_interact")) {
				NotifySubs();
			}
		}
		
		//Check for tab
		if(Input.IsActionJustPressed("ui_focus_next")) {
			EmitSignal(nameof(OpenNotebook));
		}
	}
	
	private void CheckIdle() {
		//Become idle if player stops moving
		if(Velocity == Vector2.Zero) {
			//Update state and animation
			CurrentState = PlayerStates.IDLE;
			animationState.Travel("Idle");
		}
	}
	
	/**
	 * @brief Updates the player's state according to the current actions taken
	 * @param delta, the time elapsed since the last update
	 */
	private void HandleState(float delta) {
		switch(CurrentState) {
			case PlayerStates.IDLE:
				//Rest run cooldown faster
				RunCooldown = (RunCooldown < RunTime) ? RunCooldown + (2.0f * (float)delta) : RunTime;
				
				//Check for state change
				if(Velocity != Vector2.Zero) {
					CurrentState = PlayerStates.WALKING;
					animationState.Travel("Walk");
				}
				break;
				
			case PlayerStates.WALKING:
				//Rest run cooldown
				RunCooldown = (RunCooldown < RunTime) ? RunCooldown + (float)delta : RunTime;
				
				//Check for sprint
				if(RunRequest && RunCooldown == RunTime) {
					CurrentState = PlayerStates.RUNNING;
					
					//Update animation to match state change
					animationState.Travel("Run");
				} 
		
				CheckIdle();
				break;
				
			case PlayerStates.RUNNING:
				//Burn cooldown if running
				RunCooldown -= (float)delta;
				if(RunCooldown <= 0.0f) {
					RunCooldown = 0.0f;
					CurrentState = PlayerStates.WALKING;
					
					//Update animation to match state change
					animationState.Travel("Walk");
				}
				
				CheckIdle();
				break;
				
			case PlayerStates.BLOCKED:
				animationState.Travel("Idle");
				break;
				
			default:
				//Update state and animation
				CurrentState = PlayerStates.IDLE;
				animationState.Travel("Idle");
				break;
		}
	}
	
	public override void _Ready() {
		//Initialize variables
		Speed = WalkSpeed;
		RunCooldown = RunTime;
		CurrentState = PlayerStates.IDLE;
		
		//Fetch nodes
		animation = GetNode<AnimationPlayer>("AnimationPlayer");
		animationTree = GetNode<AnimationTree>("AnimationTree");
		animationState = (AnimationNodeStateMachinePlayback)animationTree.Get("parameters/playback");
		
		if(!isCutscene) {
			EmitSignal(nameof(SlideInNotebookController));
		}

	}
	
	// Called on every physics engine tick
	public override void _Process(float delta) {
		if(isCutscene) {
			HandleCutscene(delta);
		} else {
			//Handle input
			HandleInput(delta);
		}
		
		//Update player state
		HandleState(delta);
		
		//Scale velocity and move
		Velocity = MoveAndSlide(Velocity);
	}
	
	public int _Subscribe(NPC npc) {
		subs.Add(npc);
		return nSubs++;
	}
	
	public void _Unsubscribe(NPC npc) {
		subs.Remove(npc);
		nSubs--;
	}
	
	// Finds the nearest sub to the player
	private NPC NearestSub() {
		float minDistance = float.MaxValue;
		NPC nearest = subs[0];
		
		// Iterate through all subs and keep the one with the shortest distance to player
		foreach(NPC sub in subs) {
			var distance = Position.DistanceTo(sub.Position);
			if(distance < minDistance) {
				minDistance = distance;
				nearest = sub;
			}
		}
		return nearest;
	}
	
	private void NotifySubs() {
		// Only notify the nearest sub
		var nearestNPC = NearestSub();
		if(nearestNPC.isQuestNPC) {
			EmitSignal(nameof(SendInfoToQuestNPC), this, nearestNPC);
		} else {
			nearestNPC._Notify(this);
		}
	}
	
	public void _EndDialogue() {
		CurrentState = PlayerStates.IDLE;
		
		// If the cutscene is still going, end it
		if(isCutscene) {
			isCutscene = false;
			var nearestNPC = NearestSub();
			EmitSignal(nameof(CutsceneEnd), nearestNPC);
			EmitSignal(nameof(SlideInNotebookController));
		}
	}
	
	public void _StartDialogue() {
		CurrentState = PlayerStates.BLOCKED;
		
		if(isCutscene) {
			isCutsceneConv = true;
		}
	}
	
	/**
	 * @brief Makes sure that the player can't move when the notebook is open 
	 */
	private void _on_NotebookController_pressed() {
		if(NotebookOpen) {
			NotebookOpen = false;
			//Restore the state the previous state before the notebook was opened
			CurrentState = PrevState;
		} else {
			NotebookOpen = true;
			PrevState = CurrentState;
			CurrentState = PlayerStates.BLOCKED;
		}
	}
}


