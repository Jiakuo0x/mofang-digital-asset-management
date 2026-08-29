export function Brand({ compact = false }: { compact?: boolean }) {
  return (
    <div className={`brand ${compact ? 'brand--compact' : ''}`}>
      <img src={`${import.meta.env.BASE_URL}brand/mofang-cube.png`} alt="" className="brand__mark" />
      <span>魔方数字资产管理</span>
    </div>
  )
}
