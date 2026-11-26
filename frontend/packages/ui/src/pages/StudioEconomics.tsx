import {useState} from 'react';

const currency = new Intl.NumberFormat('ru-RU', {
  style: 'currency',
  currency: 'RUB',
  maximumFractionDigits: 0,
});

const assumptions = [
  {key: 'investment', label: 'Стартовые вложения, ₽', value: 600_000, step: 10_000, max: 50_000_000},
  {key: 'sites', label: 'Платящих площадок', value: 12, step: 1, max: 1_000},
  {key: 'price', label: 'Выручка с площадки в месяц, ₽', value: 14_000, step: 500, max: 500_000},
  {key: 'variable', label: 'Расходы на площадку в месяц, ₽', value: 3_000, step: 500, max: 500_000},
  {key: 'fixed', label: 'Постоянные расходы в месяц, ₽', value: 80_000, step: 1_000, max: 5_000_000},
] as const;

type InputKey = typeof assumptions[number]['key'];
const example: Record<InputKey, number> = {
  investment: assumptions[0].value,
  sites: assumptions[1].value,
  price: assumptions[2].value,
  variable: assumptions[3].value,
  fixed: assumptions[4].value,
};
const months = [0, 3, 6, 9, 12, 18, 24];

export function StudioEconomics() {
  const [values, setValues] = useState(example);
  const {investment, sites, price, variable, fixed} = values;
  const revenue = sites * price;
  const monthlyCash = revenue - sites * variable - fixed;
  const payback = investment === 0 ? 0 : monthlyCash > 0 ? Math.ceil(investment / monthlyCash) : null;
  const rows = months.map(month => ({month, total: monthlyCash * month - investment}));
  const scale = Math.max(1, ...rows.map(row => Math.abs(row.total)));

  return <section id="model" className="studio-section economics" aria-labelledby="economics-title">
    <div className="studio-section-heading">
      <h2 id="economics-title">Экономика пилота</h2>
      <p>Редактируемый сценарий. Исходные цифры условные и не отражают выручку, цены или бюджет MysticEgo.</p>
    </div>
    <div className="economics-layout">
      <fieldset>
        <legend>Допущения</legend>
        {assumptions.map(({key, label, step, max}) => <label key={key}>
          <span>{label}</span>
          <input type="number" min="0" max={max} step={step} value={values[key]}
            onChange={event => {
              const amount = event.target.valueAsNumber;
              setValues(previous => ({...previous, [key]: Number.isFinite(amount) ? Math.min(max, Math.max(0, Math.floor(amount))) : 0}));
            }}/>
        </label>)}
        <button type="button" className="studio-button" onClick={() => setValues(example)}>Сбросить пример</button>
      </fieldset>
      <div className="economics-results">
        <dl className="economics-metrics" aria-live="polite">
          <div><dt>Выручка в месяц</dt><dd>{currency.format(revenue)}</dd></div>
          <div><dt>Поток в месяц</dt><dd>{currency.format(monthlyCash)}</dd></div>
          <div><dt>Возврат вложений</dt><dd>{payback === null ? 'Не окупается' : payback + ' мес.'}</dd></div>
        </dl>
        <figure>
          <figcaption>Накопленный денежный поток</figcaption>
          {rows.map(({month, total}) => <div className="economics-row" key={month}>
            <span>{month} мес.</span>
            <span className="economics-track" aria-hidden="true">
              <span className={total < 0 ? 'loss' : 'gain'} style={{width: Math.abs(total) / scale * 50 + '%'}}/>
            </span>
            <span>{currency.format(total)}</span>
          </div>)}
        </figure>
      </div>
    </div>
    <p className="studio-note">Модель предполагает постоянное число площадок с первого месяца. Налоги, рост продаж и сезонность не учтены. Срок окупаемости округлён вверх до полного месяца.</p>
  </section>;
}
