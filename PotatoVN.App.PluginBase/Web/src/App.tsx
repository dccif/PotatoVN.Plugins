import { createSignal, onMount, onCleanup, For, Show } from 'solid-js';

interface Game {
  id: string;
  name: string;
  image: string;
}

declare global {
  interface Window {
    chrome: any;
    potatoData?: Game[];
  }
}

function App() {
  const [games, setGames] = createSignal<Game[]>([]);
  const [hasInteracted, setHasInteracted] = createSignal(false);
  let gridRef: HTMLDivElement | undefined;
  let lastInputTime = 0;
  let animationFrameId: number;

  onMount(() => {
    if (window.potatoData) {
      setGames(window.potatoData);
    }
    
    // Auto-start polling, but interaction is needed for actual gamepad data usually
    startGamepadPolling();
    
    // Log console to C#
    const oldLog = console.log;
    console.log = (...args) => {
        oldLog(...args);
        window.chrome?.webview?.postMessage(JSON.stringify({ type: 'log', message: args.join(' ') }));
    };
  });

  onCleanup(() => {
    cancelAnimationFrame(animationFrameId);
  });

  const handleInteraction = () => {
    if (!hasInteracted()) {
      setHasInteracted(true);
      // Focus first card
      setTimeout(() => {
        const first = gridRef?.querySelector('.card') as HTMLElement;
        first?.focus();
      }, 50);
    }
  };

  const startGamepadPolling = () => {
    const poll = () => {
      const now = performance.now();
      if (now - lastInputTime > 150) {
        const gamepads = navigator.getGamepads();
        // Check ALL slots
        for (const gp of gamepads) {
          if (gp) {
            // If any button pressed or axis moved, trigger interaction state
            if (!hasInteracted()) {
                const anyButton = gp.buttons.some(b => b.pressed);
                const anyAxis = gp.axes.some(a => Math.abs(a) > 0.2);
                if (anyButton || anyAxis) handleInteraction();
            }
            
            if (hasInteracted()) {
                handleGamepadInput(gp, now);
                break; // Only process one gamepad
            }
          }
        }
      }
      animationFrameId = requestAnimationFrame(poll);
    };
    animationFrameId = requestAnimationFrame(poll);
  };

  const handleGamepadInput = (gp: Gamepad, time: number) => {
    const threshold = 0.5;
    
    const left = gp.axes[0] < -threshold || gp.buttons[14]?.pressed;
    const right = gp.axes[0] > threshold || gp.buttons[15]?.pressed;
    const up = gp.axes[1] < -threshold || gp.buttons[12]?.pressed;
    const down = gp.axes[1] > threshold || gp.buttons[13]?.pressed;
    
    const accept = gp.buttons[0]?.pressed; // A / Cross
    const back = gp.buttons[1]?.pressed;   // B / Circle

    if (left || right || up || down) {
      moveFocusGeometric(left, right, up, down);
      lastInputTime = time;
    } else if (accept) {
      const active = document.activeElement as HTMLElement;
      if (active && active.classList.contains('card')) {
        active.click();
        lastInputTime = time + 300;
      }
    } else if (back) {
      handleExit();
      lastInputTime = time + 500;
    }
  };

  const moveFocusGeometric = (left: boolean, right: boolean, up: boolean, down: boolean) => {
    if (!gridRef) return;
    const cards = Array.from(gridRef.querySelectorAll('.card')) as HTMLElement[];
    const active = document.activeElement as HTMLElement;
    
    if (!active || !cards.includes(active)) {
        cards[0]?.focus();
        return;
    }

    const currentRect = active.getBoundingClientRect();
    const currentCenter = { 
        x: currentRect.left + currentRect.width / 2, 
        y: currentRect.top + currentRect.height / 2 
    };

    let bestCandidate: HTMLElement | null = null;
    let minDistance = Infinity;

    // Filter candidates based on direction
    const candidates = cards.filter(card => {
        if (card === active) return false;
        const rect = card.getBoundingClientRect();
        const center = { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
        
        const dx = center.x - currentCenter.x;
        const dy = center.y - currentCenter.y;

        if (left) return dx < -10 && Math.abs(dy) < rect.height / 2; // Roughly same row
        if (right) return dx > 10 && Math.abs(dy) < rect.height / 2;
        if (up) return dy < -10; // Strictly above
        if (down) return dy > 10; // Strictly below
        return false;
    });

    // Find closest based on Euclidean distance
    candidates.forEach(card => {
        const rect = card.getBoundingClientRect();
        const center = { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
        const dist = Math.pow(center.x - currentCenter.x, 2) + Math.pow(center.y - currentCenter.y, 2);
        
        if (dist < minDistance) {
            minDistance = dist;
            bestCandidate = card;
        }
    });

    if (bestCandidate) {
        (bestCandidate as HTMLElement).focus();
        (bestCandidate as HTMLElement).scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
  };

  const handleExit = () => window.chrome?.webview?.postMessage({ type: 'close' });
  const handleLaunch = (id: string) => window.chrome?.webview?.postMessage({ type: 'launch', id });

  return (
    <div style={{ height: '100vh', display: 'flex', "flex-direction": 'column', "user-select": 'none' }} onClick={handleInteraction} onKeyDown={handleInteraction}>
      <Show when={!hasInteracted()}>
        <div style={{
            position: 'fixed', inset: 0, "z-index": 9999,
            background: 'rgba(0,0,0,0.85)',
            display: 'flex', "flex-direction": 'column', "justify-content": 'center', "align-items": 'center',
            color: 'white', "font-family": 'Segoe UI', "font-weight": 300
        }}>
            <h1 style={{"font-size": '48px', "margin-bottom": '20px'}}>PRESS ANY BUTTON</h1>
            <p style={{"font-size": '24px', opacity: 0.8}}>Gamepad or Keyboard or Mouse</p>
        </div>
      </Show>

      <header style={{ padding: '20px 40px', display: 'flex', "align-items": 'center', background: 'rgba(0,0,0,0.8)', "z-index": 100, "backdrop-filter": 'blur(10px)' }}>
        <button onClick={handleExit} style={{ background: 'transparent', border: '1px solid rgba(255,255,255,0.2)', color: 'white', padding: '8px 16px', "margin-right": '20px', cursor: 'pointer' }}>EXIT (B)</button>
        <h1 style={{ margin: 0, "font-size": '24px', "font-weight": 300, "text-transform": 'uppercase' }}>Library</h1>
      </header>
      <div style={{ padding: '80px 40px 20px', "overflow-y": 'auto', flex: 1 }}>
        <div ref={gridRef} style={{ display: 'grid', "grid-template-columns": 'repeat(auto-fill, minmax(200px, 1fr))', gap: '20px' }}>
          <For each={games()}>
            {(game) => (
              <div 
                class="card"
                tabIndex={0}
                onClick={() => handleLaunch(game.id)}
                style={{
                  "background-color": 'var(--card-bg)', "border-radius": '4px', overflow: 'hidden', cursor: 'pointer', "aspect-ratio": '2/3', position: 'relative',
                  "content-visibility": 'auto', "contain-intrinsic-size": '200px 300px'
                }}
              >
                <img 
                  loading="lazy" 
                  src={game.image} 
                  style={{ width: '100%', height: '100%', "object-fit": 'cover' }} 
                  onError={(e) => e.currentTarget.src = 'https://via.placeholder.com/200?text=Error'}
                />
                <div style={{ position: 'absolute', bottom: 0, left: 0, right: 0, background: 'rgba(0,0,0,0.8)', padding: '10px' }}>
                  {game.name}
                </div>
              </div>
            )}
          </For>
        </div>
      </div>
    </div>
  );
}

export default App;