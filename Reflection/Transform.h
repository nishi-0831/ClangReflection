#include "Transform.generated.h"
#include "Hoge.h"
#define MT_PROPERTY()
#define MT_FUNCTION()
#define MT_COMPONENT()
#define MT_GENERATED_BODY()



MT_COMPONENT()
class Transform
{
public:
	MT_GENERATED_BODY()
protected:
	void OnPostRestore() override;
private:
	MT_PROPERTY()
	Vector3 position_;
	Hoge hoge_;
	// 既存のコード(省略)
	//Matrix4x4 matrixWorld_;
};