using Godot;
using System;

public partial class Player : CharacterBody2D
{
	[Export]
	public float Speed = 300f;
	private float survivalTime = 0f;
	private Label scoreLabel;
	private PackedScene bulletScene;
	private Vector2 lastDirection = Vector2.Right;
	private bool isDead = false;
	
	public override void _Ready()
	{
		MyProfiler.Begin();
		scoreLabel = GetTree().Root.GetNode<Label>("Main/UI/ScoreLabel");
		bulletScene = GD.Load<PackedScene>("res://Bullet.tscn");
		MyProfiler.End();
	}
	public override void _PhysicsProcess(double delta)
	{
		MyProfiler.Begin();
		survivalTime += (float)delta;
		scoreLabel.Text = "Score: " + ((int)survivalTime).ToString();
		Vector2 direction = Vector2.Zero;

		if (Input.IsActionPressed("ui_right"))
			direction.X += 1;

		if (Input.IsActionPressed("ui_left"))
			direction.X -= 1;

		if (Input.IsActionPressed("ui_down"))
			direction.Y += 1;

		if (Input.IsActionPressed("ui_up"))
			direction.Y -= 1;
			
		if (Input.IsActionJustPressed("ui_accept"))
		{
			Shoot();
		}

		direction = direction.Normalized();
		if (direction != Vector2.Zero)
		{
			lastDirection = direction;
		}

		Velocity = direction * Speed;

		MoveAndSlide();
		MyProfiler.End();

		MyProfiler.Begin("Collision");
		
		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			KinematicCollision2D collision = GetSlideCollision(i);
			
			if (collision.GetCollider() is Enemy)
			{
				Die();
			}
		}

		MyProfiler.End("Collision");
	}

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
		{
			MyProfiler.Summary();
		}
    }

private void Die()
{
	GD.Print("Touched");
	if (isDead)        
		return;
		
	isDead = true;
	GD.Print ("GAME OVER");
	GetTree().ReloadCurrentScene();
	}


private void Shoot()
{
	Bullet bullet = bulletScene.Instantiate<Bullet>();
	
	bullet.Position = Position;
	bullet.Direction = lastDirection;
	GetTree().CurrentScene.AddChild(bullet);
}
}
