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
		[[reflect]]
		int value;
		EnumType e;
		Sample() {};
		~Sample() {};
		[[noreturn]]
		[[noreturn]]
		void f() {};
	private:
		
	};
}