﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace JuliaSet {
  class IteratedEventArgs : EventArgs {
    int width, height;
    long length, curr;
    double progress;
    bool areAnyAlive, didAnyDie, isDone;

    public int Width { get { return width; } }
    public int Height { get { return height; } }
    public long Length { get { return length; } }
    public long Current { get { return curr; } }
    public double Progress { get { return progress; } }
    public bool AreAnyAlive { get { return areAnyAlive; } }
    public bool DidAnyDie { get { return didAnyDie; } }
    public bool IsDone { get { return isDone; } }

    public IteratedEventArgs(int width, int height, long length, long curr, double progress, bool areAnyAlive, bool didAnyDie, bool isDone) {
      this.width = width;
      this.height = height;
      this.length = length;
      this.curr = curr;
      this.progress = progress;
      this.areAnyAlive = areAnyAlive;
      this.didAnyDie = didAnyDie;
      this.isDone = isDone;
    }
  }

  delegate void IterResizedEvent(int width, int height, long length);

  delegate void IterIteratedEvent(Iterator sender, IteratedEventArgs e);

  delegate void IterStartedEvent(Iterator iter);

  delegate void IterCompletedEvent(Iterator iter);

  abstract class Iterator : DependencyObject {
    protected double thresh = 10, scale = 1, scalePx, ctrX = 0, ctrY = 0, offsX, offsY;
    protected int width = 1, height = 1;
    protected long length, iters = 1;
    protected bool doRepop;
    protected double[] result;
    protected bool[] isAlive;

    protected IterResizedEvent resized;
    protected IterIteratedEvent iterated;
    protected IterStartedEvent started;
    protected IterCompletedEvent completed;

    public abstract bool IsRunning { get; }

    public double Thresh {
      get { return thresh; }
      set { thresh = value; }
    }

    public long Iterations {
      get { return iters; }
      set { iters = value; }
    }

    public int Width {
      get { return (int)GetValue(WidthProperty); }
      set { SetValue(WidthProperty, value); }
    }

    public int Height {
      get { return (int)GetValue(HeightProperty); }
      set { SetValue(HeightProperty, value); }
    }

    public long Length {
      get { return length; }
    }

    public double Scale {
      get { return scale; }
      set { scale = value; Resize(); }
    }

    public double ScalePx {
      get { return scalePx; }
    }

    public double CenterX {
      get { return ctrX; }
      set { ctrX = value; Resize(); }
    }

    public double CenterY {
      get { return ctrY; }
      set { ctrY = value; Resize(); }
    }

    public double OffsX {
      get { return offsX; }
    }

    public double OffsY {
      get { return offsY; }
    }

    public ReadOnlyCollection<double> Result {
      get { return Array.AsReadOnly(result); }
    }

    public ReadOnlyCollection<bool> IsAlive {
      get { return Array.AsReadOnly(isAlive); }
    }

    public event IterResizedEvent Resized {
      add { resized += value; }
      remove { resized -= value; }
    }

    public event IterIteratedEvent Iterated {
      add { iterated += value; }
      remove { iterated -= value; }
    }

    public event IterStartedEvent Started {
      add { started += value; }
      remove { started -= value; }
    }

    public event IterCompletedEvent Completed {
      add { completed += value; }
      remove { completed -= value; }
    }

    public Iterator(long iters, double thresh) {
      this.Iterations = iters;
      this.Thresh = thresh;

      Resize();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) {
      base.OnPropertyChanged(e);

      if (e.Property.PropertyType == typeof(int)) {
        if ((int)e.NewValue != (int)e.OldValue) {
          Resize();
        }
      }
    }

    public virtual void Start() {
      if (started != null) started(this);
    }

    public double GetScalePx(double scale) {
      if (width < height)
        return scale * 2 / width;
      else
        return scale * 2 / height;
    }

    protected virtual void Resize() {
      width = Width;
      height = Height;
      length = width * height;

      if (width < height)
        scalePx = scale * 2 / width;
      else
        scalePx = scale * 2 / height;

      offsX = (ctrX / scalePx) - (width / 2);
      offsY = (ctrY / scalePx) - (height / 2);

      doRepop = true;

      if (resized != null) resized(width, height, length);
    }

    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register("Width", typeof(int), typeof(Iterator), new PropertyMetadata(0));

    public static readonly DependencyProperty HeightProperty =
        DependencyProperty.Register("Height", typeof(int), typeof(Iterator), new PropertyMetadata(0));
  }

  abstract class Iterator<T> : Iterator where T : IterFunc {
    protected T func;

    public T Func {
      get { return func; }
      set { func = value; }
    }

    public Iterator(T func, long iters, double thresh)
      : base(iters, thresh) {

      this.Func = func;
    }

  }

  abstract class SingleThreadedIterator<T> : Iterator<T> where T : IterFunc {
    Thread iterThread;

    public override bool IsRunning {
      get { return iterThread.IsAlive; }
    }

    public SingleThreadedIterator(T func, long iters, double thresh)
      : base(func, iters, thresh) {
      iterThread = new Thread(DoIterations) {
        IsBackground = true,
        Name = "JuliaSet Iterator Thread",
        Priority = ThreadPriority.AboveNormal,
      };
    }

    protected abstract void DoIterations();

    public override void Start() {
      iterThread.Start();

      base.Start();
    }
  }

  abstract class MultiThreadedIterator<T> : Iterator<T> where T : IterFunc {
    Thread[] iterThreads;
    ManualResetEvent[] syncEvents;
    protected int threadCount;

    public override bool IsRunning {
      get { return iterThreads.Any(e => e.IsAlive); }
    }

    public int ThreadCount {
      get { return threadCount; }
    }

    public MultiThreadedIterator(T func, long iters, double thresh, int threadCount)
      : base(func, iters, thresh) {
      this.threadCount = threadCount;
      iterThreads = new Thread[threadCount];
      syncEvents = new ManualResetEvent[threadCount];

      for (int i = 0; i < threadCount; i++) {
        iterThreads[i] = new Thread(DoIterations) {
          IsBackground = true,
          Name = "JuliaSet Iterator Thread " + (i + 1),
          Priority = ThreadPriority.AboveNormal,
        };

        syncEvents[i] = new ManualResetEvent(false);
      }
    }

    public MultiThreadedIterator(T func, long iters, double thresh)
      : this(func, iters, thresh, Environment.ProcessorCount) { }

    protected abstract void DoIterations(int id);

    void DoIterations(object param) {
      if (param is int) {
        int id = (param as int?).Value;

        syncEvents[id].WaitOne();

        DoIterations(id);
      }
      else throw new ArgumentException("Thread param not of type int.");
    }

    public override void Start() {
      for (int i = 0; i < threadCount; i++) {
        iterThreads[i].Start(i);
      }

      foreach(ManualResetEvent evt in syncEvents)
        evt.Set();

      base.Start();
    }
  }
}