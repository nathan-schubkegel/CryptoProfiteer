namespace CryptoProfiteer
{
  /// <summary>
  /// A double-ended queue data structure with O(1) AddFront, AddBack, RemoveFront, RemoveBack
  /// </summary>
  public class Deque<T>
  {
    private T[] _buffer;
    private int _startIndex;
    private int _count;

    public Deque()
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
    
    public void AddEnd(T item)
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
      if (_count == 0) throw new Exception("Can't remove from empty deque");

      // prevent memory leaks
      _buffer[GetBufferIndex(0)] = default;

      _count--;
      _startIndex++;
      if (_startIndex == _buffer.Length) _startIndex = 0;
    }

    public void RemoveEnd()
    {
      if (_count == 0) throw new Exception("Can't remove from empty deque");

      // prevent memory leaks
      _buffer[GetBufferIndex(_count - 1)] = default;

      _count--;
    }

    public T PeekFront()
    {
      if (_count == 0) throw new Exception("Can't peek in empty deque");
      return _buffer[GetBufferIndex(0)];
    }

    public T PeekEnd()
    {
      if (_count == 0) throw new Exception("Can't peek in empty deque");
      return _buffer[GetBufferIndex(_count - 1)];
    }
    
    public this[int index]
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
  }
}