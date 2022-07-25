using System;
using System.Collections;
using System.Collections.Generic;

namespace CryptoProfiteer
{
  /// <summary>
  /// A double-ended queue data structure with O(1) AddFront, AddBack, RemoveFront, RemoveBack
  /// </summary>
  public class Dequeue<T> : IEnumerable<T>
  {
    private T[] _buffer;
    private int _startIndex;
    private int _count;

    public Dequeue()
    {
      _buffer = new T[16];
    }

    private int GetBufferIndex(int index)
    {
      var result = _startIndex + index;
      if (result >= _buffer.Length) result -= _buffer.Length;
      return result;
    }

    public int Count => _count;
    
    private void GrowBeforeAddIfNeeded()
    {
      if (_count == _buffer.Length)
      {
        var newBuffer = new T[_buffer.Length * 2];
        for (int i = 0; i < _buffer.Length; i++)
        {
          newBuffer[i] = _buffer[GetBufferIndex(i)];
        }
        _buffer = newBuffer;
        _startIndex = 0;
      }
    }

    public void AddFront(T item)
    {
      GrowBeforeAddIfNeeded();
      var index = GetBufferIndex(0) - 1;
      if (index == -1) index = _buffer.Length - 1;
      _buffer[index] = item;
      _count++;
      _startIndex = index;
    }
    
    public void AddBack(T item)
    {
      GrowBeforeAddIfNeeded();
      _count++;
      _buffer[GetBufferIndex(_count - 1)] = item;
    }

    public void Clear()
    {
      _startIndex = 0;
      _count = 0;
      if (_buffer.Length > 16) _buffer = new T[16];
      else
      {
        // prevent memory leaks
        for (int i = 0; i < _buffer.Length; i++)
        {
          _buffer[i] = default;
        }
      }
    }

    public void RemoveFront()
    {
      if (_count == 0) throw new Exception("Can't remove from empty Dequeue");

      // prevent memory leaks
      _buffer[GetBufferIndex(0)] = default;

      _count--;
      _startIndex++;
      if (_startIndex == _buffer.Length) _startIndex = 0;
    }

    public void RemoveBack()
    {
      if (_count == 0) throw new Exception("Can't remove from empty Dequeue");

      // prevent memory leaks
      _buffer[GetBufferIndex(_count - 1)] = default;

      _count--;
    }

    public T PeekFront()
    {
      if (_count == 0) throw new Exception("Can't peek in empty Dequeue");
      return _buffer[GetBufferIndex(0)];
    }

    public T PeekBack()
    {
      if (_count == 0) throw new Exception("Can't peek in empty Dequeue");
      return _buffer[GetBufferIndex(_count - 1)];
    }
    
    public T this[int index]
    {
      get
      {
        if (index < 0 || index >= _count) throw new Exception("invalid index");
        return _buffer[GetBufferIndex(index)];
      }
      
      set
      {
        if (index < 0 || index >= _count) throw new Exception("invalid index");
        _buffer[GetBufferIndex(index)] = value;
      }
    }
    
    public IEnumerator<T> GetEnumerator()
    {
      return new Enumerator(this);
    }
    
    IEnumerator IEnumerable.GetEnumerator()
    {
      return new Enumerator(this);
    }
    
    public class Enumerator : IEnumerator<T>, IEnumerator
    {
      Dequeue<T> _thing;
      int _index = -1;
      
      public Enumerator(Dequeue<T> thing) { _thing = thing; }
      
      public bool MoveNext()
      {
        if (_index + 1 >= _thing.Count) return false;
        _index++;
        return true;
      }
      
      public T Current => _thing[_index];
      
      Object IEnumerator.Current => _thing[_index];
      
      public void Reset()
      {
        _index = -1;
      }
      
      public void Dispose()
      {
        _thing = null;
      }
    }
    
    static Dequeue()
    {
      void assert(bool condition, string message)
      {
        if (!condition) throw new Exception(message);
      }
      
      var a = new Dequeue<int>();
      a.AddFront(3);
      a.AddBack(4);
      a.AddFront(5);
      a.AddBack(6);
      assert(a.Count == 4, "count of 'a'");
      assert(a[0] == 5, "a[0] should == 5");
      assert(a[1] == 3, "a[1] should == 3");
      assert(a[2] == 4, "a[2] should == 4");
      assert(a[3] == 6, "a[3] should == 6");
      
      a.RemoveFront();
      a.RemoveBack();
      assert(a.Count == 2, "count of 'a' round 2");
      assert(a[0] == 3, "a[0] should == 3");
      assert(a[1] == 4, "a[1] should == 4");
    }
  }
}