using Godot;
using System;

public partial class Enemy: CharacterBody2D
{
	[Export]
	public float Speed = 150f;
	private Player player;
	
	public override void _Ready()
	{
		player = GetTree().Root.GetNode<Player>("Main/Player");
	}
	
	public override void _PhysicsProcess(double delta)
	{
		MyProfiler.Begin();
		if (player == null)
			return;
		Vector2 direction = (player.Position - Position).Normalized();
		Velocity = direction * Speed;
		
		MoveAndSlide();
		MyProfiler.End();
	}
}
