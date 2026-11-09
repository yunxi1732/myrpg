using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyStateMachine
{
    public EnemyState currentState {  get; private set;  }
    public void Initialize (EnemyState _startState)
    {
        if (_startState == null || !_startState.CanSimulate) return;
        currentState = _startState;
        currentState.Enter();
    }

    public void ChangeState(EnemyState _newState)
    {
        if (_newState == null || !_newState.CanSimulate) return;
        currentState?.Exit();
        currentState = _newState;
        currentState.Enter();
    }
}
