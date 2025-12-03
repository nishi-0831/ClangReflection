// Transform.generated.h
#pragma once

// DTO構造体
struct TransformDTO
{
	float position_;
};

// マクロ展開部分... 
#define MT_GENERATED_BODY_Transform() \
	public: \
	using DTO = TransformDTO; \
	\
	IMemento* SaveToMemento() \
	{ \
		TransformDTO dto; \
		dto.position_ = this->position_; \
		return new ComponentMemento<Transform, TransformDTO>(GetEntityId(), dto); \
	} \
	\
	void RestoreFromMemento(const ComponentMemento<Transform, TransformDTO>& _memento) \
	{ \
		const TransformDTO& dto = _memento.GetDTO(); \
		this->position_ = dto.position_; \
		OnPostRestore(); \
	} \
	\
	friend struct Transform_Register;

// JSONシリアライズ定義
NLOHMANN_DEFINE_TYPE_INTRUSIVE(Transform, position_)

// Memento型定義
using TransformMemento = ComponentMemento<Transform, TransformDTO>;

// ImGui表示処理登録
struct Transform_Register
{
	Transform_Register()
	{
		RegisterShowFuncHolder::Set<Transform>([](Transform* _target, const char* _name)
		{
			TypeRegistry::Instance().CallFunc(&_target->position_, "position_");
		});
	}
};

static Transform_Register transform_register;

// マクロ上書き
#define MT_GENERATED_BODY() MT_GENERATED_BODY_Transform()