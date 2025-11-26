#define UPROPERTY()
#define UFUNCTION()
namespace test
{
	class Sample1
	{
	public :
		enum class EnumType
		{
			E0,
			E1,
			E2,
		};
		UPROPERTY()
		int value;
		EnumType e;
		Sample() {};
		~Sample() {};
		UFUNCTION()
		void f() {};
	private:
		
	};
}