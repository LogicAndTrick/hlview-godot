extends Control

@onready var config : ConfigFile = ConfigFile.new()
@onready var item_list : ItemList = $VBoxContainer/MainHSplit/VBoxContainer/ItemList

var game_dir : String = ""

func _ready() -> void:
	config.load("user://hl_view.cfg")
	set_game_dir(config.get_value("History", "game_dir", ""))

func _on_file_menu_id_pressed(id: int) -> void:
	match id:
		0: open_file()

func open_file():
	var fd : FileDialog = FileDialog.new()
	fd.access = FileDialog.ACCESS_FILESYSTEM
	fd.file_mode = FileDialog.FILE_MODE_OPEN_DIR
	fd.use_native_dialog = true
	fd.dir_selected.connect(func(dir):
		set_game_dir(dir)
		fd.queue_free()
	)
	fd.canceled.connect(func(): fd.queue_free())
	add_child(fd)
	fd.popup_centered()

func set_game_dir(dir : String) -> void:
	game_dir = dir
	config.set_value("History", "game_dir", game_dir)
	config.save("user://hl_view.cfg")
	if dir and dir != '' and DirAccess.dir_exists_absolute(game_dir):
		load_directory(dir)

var files : Array[String] = []

func load_directory(dir : String) -> void:
	var d = DirAccess.open(dir + '/maps')
	if d:
		files = []
		files.append_array(Array(d.get_files()))
		files.sort_custom(func(a : String,b : String): return a.to_lower() < b.to_lower())
	else:
		files = []
	update_file_list()

@onready var filter_text_box : TextEdit = $VBoxContainer/MainHSplit/VBoxContainer/FilterTextBox

func update_file_list() -> void:
	item_list.clear()
	var filter = filter_text_box.text.to_lower()
	for map in files:
		if map.ends_with('.bsp') and (!filter or map.to_lower().contains(filter)):
			item_list.add_item(map)
	pass

func _on_filter_text_box_text_changed() -> void:
	update_file_list()

@onready var viewport : SubViewportContainer = $VBoxContainer/MainHSplit/SubViewportContainer
func _on_sub_viewport_container_mouse_entered() -> void:
	viewport.grab_focus()

func _on_sub_viewport_container_mouse_exited() -> void:
	viewport.release_focus()

@onready var item_list_box : ItemList = $VBoxContainer/MainHSplit/VBoxContainer/ItemList
@onready var bsp_node : Node3D = $VBoxContainer/MainHSplit/SubViewportContainer/SubViewport/BspNode

func _on_item_list_item_activated(index: int) -> void:
	var file_name = item_list_box.get_item_text(index)
	var path = game_dir + '/maps/' + file_name
	bsp_node.LoadFile(game_dir, path)
