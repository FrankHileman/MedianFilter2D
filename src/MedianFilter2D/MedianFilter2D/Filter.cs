using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FrankHileman.MedianFilter2D
{
	// Derived from: https://github.com/suomela/mf2d
	public sealed class Filter
	{
		const ulong ONE64 = 1;

		// Reasonable values based on benchmarks

		static int choose_blocksize_1d(int h) => 8 * (h + 2);

		static int choose_blocksize_2d(int h) => 4 * (h + 2);

		// Find nth bit that is set and return its index
		// (no such bit: output undefined)

		static int findnth64(ulong x, int n)
		{
			for (int i = 0; i < n; ++i)
				x &= x - 1;
			return BitScanForward(x);
			//return __builtin_ctzll(x);
		}

		static int popcnt64(ulong value)
		{
			// since we don't have access to BitOperations in the .net framework,
			// we use the software fallback code here:
			// https://github.com/dotnet/corert/blob/master/src/System.Private.CoreLib/shared/System/Numerics/BitOperations.cs
			const ulong c1 = 0x_55555555_55555555ul;
			const ulong c2 = 0x_33333333_33333333ul;
			const ulong c3 = 0x_0F0F0F0F_0F0F0F0Ful;
			const ulong c4 = 0x_01010101_01010101ul;

			value -= (value >> 1) & c1;
			value = (value & c2) + ((value >> 2) & c2);
			value = (((value + (value >> 4)) & c3) * c4) >> 56;

			return (int)value;
			//return __builtin_popcountll(x);
		}

		// NOTE: GCC built in functions are described here:
		// https://github.com/llvm-mirror/libcxx/blob/9dcbb46826fd4d29b1485f25e8986d36019a6dca/include/support/win32/support.h#L106-L182

		// From: https://stackoverflow.com/a/37112058/756014
		const ulong DeBruijnSequence = 0x37E84A99DAE458F;

		static readonly int[] MultiplyDeBruijnBitPosition =
		{
			0, 1, 17, 2, 18, 50, 3, 57,
			47, 19, 22, 51, 29, 4, 33, 58,
			15, 48, 20, 27, 25, 23, 52, 41,
			54, 30, 38, 5, 43, 34, 59, 8,
			63, 16, 49, 56, 46, 21, 28, 32,
			14, 26, 24, 40, 53, 37, 42, 7,
			62, 55, 45, 31, 13, 39, 36, 6,
			61, 44, 12, 35, 60, 11, 10, 9,
		};

		/// <summary>
		/// Search the mask data from least significant bit (LSB) to the most significant bit (MSB) for a set bit (1)
		/// using De Bruijn sequence approach. Warning: Will return zero for b = 0.
		/// </summary>
		/// <param name="b">Target number.</param>
		/// <returns>Zero-based position of LSB (from right to left).</returns>
		static int BitScanForward(ulong b)
		{
			Debug.Assert(b > 0, "Target number should not be zero");
			return MultiplyDeBruijnBitPosition[((ulong)((long)b & -(long)b) * DeBruijnSequence) >> 58];
		}

		public void median_filter_2d(int x, int y, int hx, int hy, int blockhint, double[] input, double[] output)
		{
			int h = Math.Max(hx, hy);
			int blocksize = blockhint > 0 ? blockhint : choose_blocksize_2d(h);
			median_filter_impl_2d(x, y, hx, hy, blocksize, input, output);
		}

		public void median_filter_1d(int x, int hx, int blockhint, double[] input, double[] output)
		{
			int blocksize = blockhint > 0 ? blockhint : choose_blocksize_1d(hx);
			median_filter_impl_1d(x, hx, blocksize, input, output);
		}

		void median_filter_impl_2d(int x, int y, int hx, int hy, int b, double[] input, double[] output)
		{
			if (2 * hx + 1 > b)
				throw new ArgumentOutOfRangeException(nameof(hx), "window too large for this block size");
			if (2 * hy + 1 > b)
				throw new ArgumentOutOfRangeException(nameof(hy), "window too large for this block size");
			var dimx = new Dim(b, x, hx);
			var dimy = new Dim(b, y, hy);
			{
				var mc = new MedCalc2D(b, dimx, dimy, input, output);
				for (int by = 0; by < dimy.count; ++by)
				{
					for (int bx = 0; bx < dimx.count; ++bx)
						mc.run(bx, by);
				}
			}
		}

		void median_filter_impl_1d(int x, int hx, int b, double[] input, double[] output)
		{
			if (2 * hx + 1 > b)
				throw new ArgumentOutOfRangeException(nameof(hx), "window too large for this block size");
			var dimx = new Dim(b, x, hx);
			{
				var mc = new MedCalc1D(b, dimx, input, output);
				for (int bx = 0; bx < dimx.count; ++bx)
					mc.run(bx);
			}
		}

		sealed class Dim
		{
			public Dim(int b, int size, int h)
			{
				this.size = size;
				this.h = h;
				step = calc_step(b, h);
				count = calc_count(b, size, h);
				Debug.Assert(2 * h + 1 < b);
				Debug.Assert(count >= 1);
				Debug.Assert(2 * h + count * step >= size);
				Debug.Assert(2 * h + (count - 1) * step < size || count == 1);
			}

			public readonly int size;
			public readonly int h;
			public readonly int step;
			public readonly int count;

			static int calc_step(int b, int h) => b - 2 * h;

			static int calc_count(int b, int size, int h)
			{
				if (size <= b)
					return 1;
				int interior = size - 2 * h;
				int step = calc_step(b, h);
				return (interior + step - 1) / step;
			}
		}

		sealed class BDim
		{
			public BDim(Dim dim) => this.dim = dim;

			public void set(int i)
			{
				bool is_first = i == 0;
				bool is_last = i + 1 == dim.count;
				start = dim.step * i;
				int end = is_last
					? dim.size
					: 2 * dim.h + (i + 1) * dim.step;
				size = end - start;
				b0 = is_first ? 0 : dim.h;
				b1 = is_last ? size : size - dim.h;
			}

			// The window around point v is [w0(v), w1(v)).
			// 0 <= w0(v) <= v < w1(v) <= size
			public int w0(int v)
			{
				Debug.Assert(b0 <= v);
				Debug.Assert(v < b1);
				return Math.Max(0, v - dim.h);
			}

			public int w1(int v)
			{
				Debug.Assert(b0 <= v);
				Debug.Assert(v < b1);
				return Math.Min(v + 1 + dim.h, size);
			}

			// Block i is located at coordinates [start, end) in the image.
			// Within the block, median is needed for coordinates [b0, b1).
			// 0 <= start < end < dim.size
			// 0 <= b0 < b1 < size <= dim.b
			public readonly Dim dim;
			public int start;
			public int size;
			public int b0;
			public int b1;
		}

		sealed class Window
		{
			public Window(int bb)
			{
				words = get_words(bb);
				buf = new ulong[words];
			}

			public void clear()
			{
				for (int i = 0; i < words; ++i)
					buf[i] = 0;
				half[0] = 0;
				half[1] = 0;
				p = words / 2;
			}

			public void update(int op, int s)
			{
				Debug.Assert(op == -1 || op == +1);
				int i = s >> WORD_SHIFT;
				int j = s & WORD_MASK;
				if (op == +1)
					Debug.Assert((buf[i] & (ONE64 << j)) == 0);
				else
					Debug.Assert((buf[i] & (ONE64 << j)) != 0);
				buf[i] ^= ONE64 << j;
				int halfIndex = i >= p ? 1 : 0;
				half[halfIndex] += op;
			}

			public int size() => half[0] + half[1];

			public int find(int goal)
			{
				while (half[0] > goal)
				{
					--p;
					half[0] -= popcnt64(buf[p]);
					half[1] += popcnt64(buf[p]);
				}
				while (half[0] + popcnt64(buf[p]) <= goal)
				{
					half[0] += popcnt64(buf[p]);
					half[1] -= popcnt64(buf[p]);
					++p;
				}
				int n = goal - half[0];
				Debug.Assert(0 <= n && n < popcnt64(buf[p]));
				int j = findnth64(buf[p], n);
				return (p << WORD_SHIFT) | j;
			}

			static int get_words(int bb)
			{
				Debug.Assert(bb >= 1);
				return (bb + WORD_SIZE - 1) / WORD_SIZE;
			}

			const int WORD_SHIFT = 6;
			const int WORD_SIZE = 1 << WORD_SHIFT;
			const int WORD_MASK = WORD_SIZE - 1;

			// Size of buf.
			readonly int words;
			// Bit number s is on iff element s is inside the window.
			readonly ulong[] buf;
			// half[0] = popcount of buf[0] ... buf[p-1]
			// half[1] = popcount of buf[p] ... buf[words-1]
			readonly int[] half = new int[2];
			// The current guess is that the median is in buf[p].
			int p;
		}

		sealed class WindowRank
		{
			public WindowRank(int bb)
			{
				sorted = new Entry[bb];
				rank = new int[bb];
				window = new Window(bb);
				//this.bb = bb;
			}

			public void init_start() => size = 0;

			public void init_feed(double value, int slot)
			{
				if (double.IsNaN(value))
					rank[slot] = NAN_MARKER;
				else
				{
					sorted[size] = new Entry(value, slot);
					++size;
				}
			}

			public void init_finish()
			{
				Array.Sort(sorted, 0, size);
				for (int i = 0; i < size; ++i)
					rank[sorted[i].Slot] = i;
			}

			public void clear() => window.clear();

			public void update(int op, int slot)
			{
				int s = rank[slot];
				if (s != NAN_MARKER)
					window.update(op, s);
			}

			public double get_med()
			{
				int total = window.size();
				if (total == 0)
					return double.NaN;
				int goal1 = (total - 1) / 2;
				int goal2 = (total - 0) / 2;
				int med1 = window.find(goal1);
				double value = sorted[med1].Value;
				if (goal2 != goal1)
				{
					int med2 = window.find(goal2);
					Debug.Assert(med2 > med1);
					value += sorted[med2].Value;
					value /= 2;
				}
				return value;
			}

			readonly Entry[] sorted;
			readonly int[] rank;
			readonly Window window;
			//readonly int bb; // not used
			int size;
			const int NAN_MARKER = -1;

			readonly struct Entry : IComparable<Entry>, IEquatable<Entry>
			{
				public Entry(double value, int slot)
				{
					Value = value;
					Slot = slot;
				}

				public readonly double Value;
				public readonly int Slot;

				public int CompareTo(Entry other)
				{
					// For std:pair, where Value is first:
					// (a, b) < (c, d)  <=>  a < c || (a == c && b < d)
					int ret = Value.CompareTo(other.Value);
					return ret == 0 ? Slot.CompareTo(other.Slot) : ret;
				}

				public bool Equals(Entry other) => Value == other.Value && Slot == other.Slot;
			}
		}

		sealed class MedCalc2D
		{
			public MedCalc2D(int b, Dim dimx, Dim dimy, double[] input,
				double[] output)
			{
				wr = new WindowRank(b * b);
				bx = new BDim(dimx);
				by = new BDim(dimy);
				this.input = input;
				this.output = output;
			}

			public void run(int bx, int by)
			{
				this.bx.set(bx);
				this.by.set(by);
				calc_rank();
				medians();
			}

			void calc_rank()
			{
				wr.init_start();
				for (int y = 0; y < by.size; ++y)
				{
					for (int x = 0; x < bx.size; ++x)
						wr.init_feed(input[coord(x, y)], pack(x, y));
				}
				wr.init_finish();
			}

			void medians()
			{
#if NAIVE
				for (int y = by.b0; y < by.b1; ++y)
				{
					for (int x = bx.b0; x < bx.b1; ++x)
					{
						wr.clear();
						update_block(+1, bx.w0(x), bx.w1(x), by.w0(y), by.w1(y));
						set_med(x, y);
					}
				}
#else
				wr.clear();
				int x = bx.b0;
				int y = by.b0;
				update_block(+1, bx.w0(x), bx.w1(x), by.w0(y), by.w1(y));
				set_med(x, y);
				bool down = true;
				while (true)
				{
					bool right = false;
					if (down)
					{
						if (y + 1 == by.b1)
						{
							right = true;
							down = false;
						}
					}
					else
					{
						if (y == by.b0)
						{
							right = true;
							down = true;
						}
					}
					if (right)
					{
						if (x + 1 == bx.b1)
							break;
					}
					if (right)
					{
						update_block(-1, bx.w0(x), bx.w0(x + 1), by.w0(y), by.w1(y));
						++x;
						update_block(+1, bx.w1(x - 1), bx.w1(x), by.w0(y), by.w1(y));
					}
					else if (down)
					{
						update_block(-1, bx.w0(x), bx.w1(x), by.w0(y), by.w0(y + 1));
						++y;
						update_block(+1, bx.w0(x), bx.w1(x), by.w1(y - 1), by.w1(y));
					}
					else
					{
						update_block(-1, bx.w0(x), bx.w1(x), by.w1(y - 1), by.w1(y));
						--y;
						update_block(+1, bx.w0(x), bx.w1(x), by.w0(y), by.w0(y + 1));
					}
					set_med(x, y);
				}
#endif
			}

			void update_block(int op, int x0, int x1, int y0, int y1)
			{
				for (int y = y0; y < y1; ++y)
				{
					for (int x = x0; x < x1; ++x)
						wr.update(op, pack(x, y));
				}
			}

			void set_med(int x, int y)
				=> output[coord(x, y)] = wr.get_med();

			int pack(int x, int y) => y * bx.size + x;

			int coord(int x, int y)
				=> (y + by.start) * bx.dim.size + x + bx.start;

			readonly WindowRank wr;
			readonly BDim bx;
			readonly BDim by;
			readonly double[] input;
			readonly double[] output;
		}

		sealed class MedCalc1D
		{
			public MedCalc1D(int b, Dim dimx, double[] input, double[] output)
			{
				wr = new WindowRank(b);
				bx = new BDim(dimx);
				this.input = input;
				this.output = output;
			}

			public void run(int bx)
			{
				this.bx.set(bx);
				calc_rank();
				medians();
			}

			void calc_rank()
			{
				wr.init_start();
				for (int x = 0; x < bx.size; ++x)
					wr.init_feed(input[coord(x)], pack(x));
				wr.init_finish();
			}

			void medians()
			{
#if NAIVE
				for (int x = bx.b0; x < bx.b1; ++x)
				{
					wr.clear();
					update_block(+1, bx.w0(x), bx.w1(x));
					set_med(x);
				}
#else
				wr.clear();
				int x = bx.b0;
				update_block(+1, bx.w0(x), bx.w1(x));
				set_med(x);
				while (x + 1 < bx.b1)
				{
					if (x >= bx.dim.h)
						wr.update(-1, pack(x - bx.dim.h));
					++x;
					if (x + bx.dim.h < bx.size)
						wr.update(+1, pack(x + bx.dim.h));
					set_med(x);
				}
#endif
			}

			void update_block(int op, int x0, int x1)
			{
				for (int x = x0; x < x1; ++x)
					wr.update(op, pack(x));
			}

			void set_med(int x) => output[coord(x)] = wr.get_med();

			int pack(int x) => x;

			int coord(int x) => x + bx.start;

			readonly WindowRank wr;
			readonly BDim bx;
			readonly double[] input;
			readonly double[] output;
		}
	}
}
