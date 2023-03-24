# MedianFilter2D: Fast Median Filter for Floating Point Data, 1D and 2D
This is a C# port of the filtering code in the following repository:
https://github.com/suomela/mf2d

In my own search for algorithms, I found this to be one of the better algorithms. Thank you Jukka Suomela.
## Usage
In the spirit of the C++ code, everthing is contained in one file, Filter.cs. You can either use the dll output of the project as a library, or put Filter.cs into your own project.

While targeting the .NET Framework 4.8, it will compile for .NET (Core), but you may wish to change the bit manipulation methods to use those available on that platform.

The Filter.cs class has xml documenation for public methods.
## Testing
I have only ported the unit tests to C#. The other tests require the I/O and command line code, not ported. My usage of this library indcates it is fast and correct.
