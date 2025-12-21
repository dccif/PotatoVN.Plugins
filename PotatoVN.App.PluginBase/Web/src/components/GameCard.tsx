import { createSignal, Show } from 'solid-js';

interface Props {
  game: {
    id: string;
    name: string;
    image: string;
  };
  onClick: () => void;
}

const GameCard = (props: Props) => {
  const [isHovered, setIsHovered] = createSignal(false);
  const placeholder = `https://via.placeholder.com/200x300/2d3440/ffffff?text=${encodeURIComponent(props.game.name)}`;

  return (
    <div
      tabIndex={0}
      onClick={props.onClick}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      onFocus={() => setIsHovered(true)}
      onBlur={() => setIsHovered(false)}
      onKeyDown={(e) => {
        if (e.key === 'Enter') props.onClick();
      }}
      style={{
        "background-color": 'var(--card-bg)',
        "border-radius": '4px',
        overflow: 'hidden',
        cursor: 'pointer',
        position: 'relative',
        "aspect-ratio": '2 / 3',
        "box-shadow": isHovered() ? '0 10px 20px rgba(0,0,0,0.5)' : '0 4px 6px rgba(0,0,0,0.3)',
        transform: isHovered() ? 'scale(1.05)' : 'scale(1)',
        outline: isHovered() ? '3px solid var(--focus-border)' : 'none',
        transition: 'transform 0.2s, box-shadow 0.2s, outline 0.1s',
        "z-index": isHovered() ? 10 : 1
      }}
    >
      <img
        src={props.game.image || placeholder}
        alt={props.game.name}
        onError={(e) => (e.currentTarget.src = placeholder)}
        style={{
          width: '100%',
          height: '100%',
          "object-fit": 'cover',
          display: 'block'
        }}
      />
      
      <div style={{
        position: 'absolute',
        bottom: 0,
        left: 0,
        right: 0,
        background: 'linear-gradient(transparent, rgba(0,0,0,0.9))',
        padding: '20px 10px 10px',
        opacity: isHovered() ? 1 : 0,
        transition: 'opacity 0.2s'
      }}>
        <div style={{
          "font-size": '16px',
          "font-weight": 600,
          "margin-bottom": '4px',
          "text-shadow": '0 2px 4px rgba(0,0,0,0.8)',
          "white-space": 'nowrap',
          overflow: 'hidden',
          "text-overflow": 'ellipsis'
        }}>
          {props.game.name}
        </div>
      </div>
    </div>
  );
};

export default GameCard;
