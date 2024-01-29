extends Camera3D

@export var enabled : bool = true
@export var allow_mouse_capture : bool = true
@export var sensitivity : float = 0.25
@export var top_speed : float = 5
@export var acceleration : float = 0.1

var _freelook_toggled = false
var _currently_freelooking = false

@onready var viewport : SubViewportContainer = $"../.."

func _input(event: InputEvent) -> void:
	if event is InputEventMouseMotion: perform_freelook(event)

func _process(delta: float) -> void:
	if not viewport.has_focus():
		return
	if Input.is_action_just_pressed('toggle_freelook'):
		_freelook_toggled = not _freelook_toggled
	_currently_freelooking = _freelook_toggled or Input.is_action_pressed('hold_freelook')
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED if _currently_freelooking and allow_mouse_capture else Input.MOUSE_MODE_VISIBLE
	perform_movement(delta);

func perform_freelook(event : InputEventMouseMotion):
	if not _currently_freelooking: return
	var movement = event.relative
	if movement != Vector2.ZERO:
		global_rotation_degrees.y = global_rotation_degrees.y - (movement.x * sensitivity)
		global_rotation_degrees.x = clamp(global_rotation_degrees.x - (movement.y * sensitivity), -85, 85)
		# todo this seems a bit broken, fix later

var current_speed : Vector2 = Vector2.ZERO

func perform_movement(delta: float):
	var speed_f : float
	var speed_r : float

	var forward = Input.get_action_strength('forward') - Input.get_action_strength('back')
	if forward != 0:
		var i = 1
	if forward == 0: speed_f = 0
	if sign(forward) == sign(current_speed.x): speed_f = clamp(current_speed.x + acceleration * sign(forward), -top_speed, top_speed)
	else: speed_f = acceleration * sign(forward)

	var right = Input.get_action_strength('right') - Input.get_action_strength('left')
	if right == 0: speed_r = 0
	if sign(right) == sign(current_speed.y): speed_r = clamp(current_speed.y + acceleration * sign(right), -top_speed, top_speed)
	else: speed_r = acceleration * sign(right)

	current_speed = Vector2(speed_f, speed_r)
	var cam_forward = (Vector3(0, 1, 0) * transform).normalized()
	#position += forward * cam_forward
	translate(Vector3(abs(speed_r) * right, 0, -abs(speed_f) * forward))
	#print(cam_forward)
	pass
