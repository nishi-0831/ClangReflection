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
		Sample() {};
		~Sample() {};
	private:
		int value;
		EnumType e;
	};
}