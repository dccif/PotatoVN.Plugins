import { createSignal, onMount, onCleanup, For } from 'solid-js';

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
  let gridRef: HTMLDivElement | undefined;
  let lastInputTime = 0;
  let animationFrameId: number;

  onMount(() => {
    if (window.potatoData) {
      setGames(window.potatoData);
      // Auto-focus first card after render
      setTimeout(() => {
        const firstCard = gridRef?.querySelector('.card') as HTMLElement;
        firstCard?.focus();
      }, 100);
    }

    startGamepadPolling();
  });

  onCleanup(() => {
    cancelAnimationFrame(animationFrameId);
  });

  const startGamepadPolling = () => {
    const poll = () => {
      const now = performance.now();
      // Poll every frame, but limit input rate (e.g., every 150ms for navigation)
      if (now - lastInputTime > 150) {
        const gamepads = navigator.getGamepads();
        // Use the first active gamepad
        const gp = gamepads[0] || gamepads[1] || gamepads[2] || gamepads[3];

        if (gp) {
          handleGamepadInput(gp, now);
        }
      }
      animationFrameId = requestAnimationFrame(poll);
    };
    animationFrameId = requestAnimationFrame(poll);
  };

  const handleGamepadInput = (gp: Gamepad, time: number) => {
    const threshold = 0.5;
    
    // Axes: 0=LeftStickX, 1=LeftStickY. Buttons: 12=Up, 13=Down, 14=Left, 15=Right
    // A=0, B=1
    
    const left = gp.axes[0] < -threshold || gp.buttons[14]?.pressed;
    const right = gp.axes[0] > threshold || gp.buttons[15]?.pressed;
    const up = gp.axes[1] < -threshold || gp.buttons[12]?.pressed;
    const down = gp.axes[1] > threshold || gp.buttons[13]?.pressed;
    const accept = gp.buttons[0]?.pressed; // A / Cross
    const back = gp.buttons[1]?.pressed;   // B / Circle

    if (left || right || up || down) {
      moveFocus(left, right, up, down);
      lastInputTime = time;
    } else if (accept) {
      const active = document.activeElement as HTMLElement;
      if (active && active.classList.contains('card')) {
        active.click();
        lastInputTime = time + 300; // Longer debounce for actions
      }
    } else if (back) {
      handleExit();
      lastInputTime = time + 500;
    }
  };

  const moveFocus = (left: boolean, right: boolean, up: boolean, down: boolean) => {
    if (!gridRef) return;
    
    const cards = Array.from(gridRef.querySelectorAll('.card')) as HTMLElement[];
    if (cards.length === 0) return;

    const activeIndex = cards.indexOf(document.activeElement as HTMLElement);
    if (activeIndex === -1) {
      cards[0].focus();
      return;
    }

    // Calculate columns based on visual layout
    // We assume cards are roughly same width.
    const containerWidth = gridRef.clientWidth;
    const cardWidth = cards[0].getBoundingClientRect().width;
    // Get gap from computed style if possible, or estimate. 
    // Grid gap is 20px. Card width approx 200px (minmax).
    // Better way: compare 'top' coordinates.
    
    // Simple estimation:
    const columns = Math.floor(containerWidth / (cardWidth + 20)); // 20 is gap roughly
    // Or more robustly: find how many have the same offsetTop as the first one
    let cols = 1;
    const firstTop = cards[0].offsetTop;
    for(let i=1; i<cards.length; i++) {
        if (cards[i].offsetTop === firstTop) cols++;
        else break;
    }

    let nextIndex = activeIndex;

    if (left) nextIndex = activeIndex - 1;
    if (right) nextIndex = activeIndex + 1;
    if (up) nextIndex = activeIndex - cols;
    if (down) nextIndex = activeIndex + cols;

    // Boundary checks
    if (nextIndex < 0) nextIndex = 0;
    if (nextIndex >= cards.length) nextIndex = cards.length - 1;

    if (nextIndex !== activeIndex) {
      cards[nextIndex].focus();
      cards[nextIndex].scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
  };

  const handleExit = () => window.chrome?.webview?.postMessage({ type: 'close' });
  const handleLaunch = (id: string) => window.chrome?.webview?.postMessage({ type: 'launch', id });

  return (
    <div style={{ height: '100vh', display: 'flex', "flex-direction": 'column' }}>
      <header style={{ padding: '20px 40px', display: 'flex', "align-items": 'center', background: 'rgba(0,0,0,0.8)', "z-index": 100, "backdrop-filter": 'blur(10px)' }}>
        <button onClick={handleExit} style={{ background: 'transparent', border: '1px solid rgba(255,255,255,0.2)', color: 'white', padding: '8px 16px', "margin-right": '20px', cursor: 'pointer' }}>EXIT (B)</button>
        <h1 style={{ margin: 0, "font-size": '24px', "font-weight": 300, "text-transform": 'uppercase' }}>Library</h1>
      </header>
      <div style={{ padding: '80px 40px 20px', "overflow-y": 'auto', flex: 1 }}>
        <div ref={gridRef} style={{ display: 'grid', "grid-template-columns": 'repeat(auto-fill, minmax(200px, 1fr))', gap: '20px' }}>
          <For each={games()} fallback={<div style={{color:'#aaa'}}>Loading...</div>}>
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
